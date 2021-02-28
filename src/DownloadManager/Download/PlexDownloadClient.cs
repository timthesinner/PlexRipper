﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using FluentResults;
using MediatR;
using PlexRipper.Application.Common;
using PlexRipper.Application.PlexDownloads;
using PlexRipper.Domain;
using PlexRipper.DownloadManager.Common;

namespace PlexRipper.DownloadManager.Download
{
    /// <summary>
    /// The PlexDownloadClient handles a single <see cref="DownloadTask"/> at a time and
    /// manages the <see cref="DownloadWorker"/>s responsible for the multi-threaded downloading.
    /// </summary>
    public class PlexDownloadClient : IDisposable
    {
        #region Fields

        private readonly Subject<DownloadProgress> _downloadProgressChanged = new Subject<DownloadProgress>();

        private readonly Subject<DownloadTask> _downloadTaskChanged = new Subject<DownloadTask>();

        private readonly Subject<DownloadWorkerLog> _downloadWorkerLog = new Subject<DownloadWorkerLog>();

        private readonly List<DownloadWorker> _downloadWorkers = new List<DownloadWorker>();

        private readonly IMediator _mediator;

        private readonly IFileSystem _fileSystem;

        private readonly Func<DownloadWorkerTask, DownloadWorker> _downloadWorkerFactory;

        private readonly EventLoopScheduler _timeThreadContext = new EventLoopScheduler();

        private IDisposable _workerProgressSubscription;

        private bool _isSetup;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexDownloadClient"/> class.
        /// </summary>
        /// <param name="downloadTask">The <see cref="DownloadTask"/> to start executing.</param>
        /// <param name="mediator"></param>
        /// <param name="fileSystem">Used to get fileStreams in which to store the download data.</param>
        /// <param name="httpClientFactory"></param>
        /// <param name="downloadWorkerFactory"></param>
        public PlexDownloadClient(DownloadTask downloadTask, IMediator mediator, IFileSystem fileSystem,
            Func<DownloadWorkerTask, DownloadWorker> downloadWorkerFactory)
        {
            _mediator = mediator;
            _fileSystem = fileSystem;
            _downloadWorkerFactory = downloadWorkerFactory;
            DownloadTask = downloadTask;
            DownloadStatus = downloadTask.DownloadStatus;
        }

        #endregion

        #region Properties

        public DateTime DownloadStartAt { get; internal set; }

        public DownloadStatus DownloadStatus
        {
            get => DownloadTask.DownloadStatus;
            internal set => DownloadTask.DownloadStatus = value;
        }

        public DownloadTask DownloadTask { get; internal set; }

        /// <summary>
        /// The ClientId/DownloadTaskId is always the same id that is assigned to the <see cref="DownloadTask"/>.
        /// </summary>
        public int DownloadTaskId => DownloadTask.Id;

        /// <summary>
        /// In how many parts/segments should the media be downloaded.
        /// </summary>
        public long Parts { get; set; } = 1;

        public long TotalBytesToReceive => DownloadTask.DataTotal;

        #region Observables

        public IObservable<DownloadProgress> DownloadProgressChanged => _downloadProgressChanged.AsObservable();

        public IObservable<DownloadTask> DownloadTaskChanged => _downloadTaskChanged.AsObservable();

        public IObservable<DownloadWorkerLog> DownloadWorkerLog => _downloadWorkerLog.AsObservable();

        #endregion

        #endregion

        #region Methods

        #region Private

        /// <summary>
        /// Releases the unmanaged resources used by the HttpClient and optionally disposes of the managed resources.
        /// </summary>
        /// <param name="disposing">Is currently disposing.</param>
        public void Dispose()
        {
            _downloadProgressChanged?.Dispose();
            _downloadTaskChanged?.Dispose();
            _downloadWorkerLog?.Dispose();
        }

        private void SetupSubscriptions()
        {
            if (!_downloadWorkers.Any())
            {
                Log.Warning("No download workers have been made yet, cannot setup subscriptions.");
                return;
            }

            // On download progress
            _downloadWorkers
                .Select(x => x.DownloadWorkerProgress)
                .CombineLatest()
                .Sample(TimeSpan.FromMilliseconds(1000), _timeThreadContext)
                .Subscribe(OnDownloadProgressChanged);

            // On download worker log
            _downloadWorkers
                .Select(x => x.DownloadWorkerLog)
                .Merge()
                .Subscribe(OnDownloadWorkerLog);

            // On download task change
            _downloadWorkers
                .Select(x => x.DownloadWorkerTaskChanged)
                .CombineLatest()
                .Subscribe(OnDownloadWorkerTaskChange);
        }

        /// <summary>
        /// Calls Dispose on all DownloadWorkers and clears the downloadWorkersList.
        /// </summary>
        /// <returns>Is successful.</returns>
        private async Task ClearDownloadWorkers()
        {
            await Task.WhenAll(_downloadWorkers.Select(x => x.DisposeAsync()).ToList());
            _downloadWorkers.Clear();
            Log.Debug($"DownloadWorkers have been disposed for {DownloadTask.DownloadPath}");
        }

        #region Subscriptions

        private void OnDownloadProgressChanged(IList<IDownloadWorkerProgress> progressList)
        {
            if (_downloadProgressChanged.IsDisposed)
            {
                return;
            }

            var orderedList = progressList.ToList().OrderBy(x => x.Id).ToList();
            StringBuilder builder = new StringBuilder();
            foreach (var progress in orderedList)
            {
                builder.Append($"({progress.Id} - {progress.Percentage} {progress.DownloadSpeedFormatted}) + ");
            }

            // Remove the last " + "
            if (builder.Length > 3)
            {
                builder.Length -= 3;
            }

            var downloadProgress = new DownloadProgress(orderedList)
            {
                Id = DownloadTaskId,
                PlexLibraryId = DownloadTask.PlexLibraryId,
                PlexServerId = DownloadTask.PlexServerId,
            };
            builder.Append($" = ({downloadProgress.DownloadSpeedFormatted} - {downloadProgress.TimeRemaining})");
            Log.Debug(builder.ToString());

            _downloadProgressChanged.OnNext(downloadProgress);
        }

        private void OnDownloadWorkerLog(DownloadWorkerLog downloadWorkerLog)
        {
            _downloadWorkerLog.OnNext(downloadWorkerLog);
        }

        private void OnDownloadWorkerTaskChange(IList<DownloadWorkerTask> downloadWorkerTasks)
        {
            var clientStatus = downloadWorkerTasks.Select(x => x.DownloadStatus).ToList();

            // If there is any error then set client to error state
            if (clientStatus.Any(x => x == DownloadStatus.Error))
            {
                DownloadStatus = DownloadStatus.Error;
            }

            if (clientStatus.Any(x => x == DownloadStatus.Downloading))
            {
                DownloadStatus = DownloadStatus.Downloading;
            }

            if (clientStatus.All(x => x == DownloadStatus.Completed))
            {
                DownloadStatus = DownloadStatus.Completed;
            }

            DownloadTask.DownloadWorkerTasks = downloadWorkerTasks.ToList();
            _downloadTaskChanged.OnNext(DownloadTask);

            if (DownloadStatus == DownloadStatus.Completed)
            {
                _downloadTaskChanged.OnCompleted();
                _downloadProgressChanged.OnCompleted();
                _downloadWorkerLog.OnCompleted();
            }
        }

        // private async Task OnDownloadComplete(IList<DownloadWorkerComplete> completeList)
        // {
        //     _timeThreadContext.Dispose();
        //
        //     var orderedList = completeList.ToList().OrderBy(x => x.Id).ToList();
        //     StringBuilder builder = new StringBuilder();
        //     foreach (var progress in orderedList)
        //     {
        //         builder.Append($"({progress.Id} - {progress.FileName} download completed!) + ");
        //         if (!progress.ReceivedAllBytes)
        //         {
        //             var msg = $"Did not receive the correct number of bytes for download worker {progress.Id}.";
        //             msg +=
        //                 $" Received {progress.BytesReceived} and not {progress.BytesReceivedGoal} with a difference of {progress.BytesReceivedGoal - progress.BytesReceived}";
        //             Log.Error(msg);
        //         }
        //     }
        //
        //     // Remove the last " + "
        //     if (builder.Length > 3)
        //     {
        //         builder.Length -= 3;
        //     }
        //
        //     Log.Debug(builder.ToString());
        //     Log.Debug($"Download of {DownloadTask.FileName} finished!");
        //
        //     await ClearDownloadWorkers();
        // }

        #endregion

        #endregion

        #region Public

        #region Commands

        /// <summary>
        /// Starts the download workers for the <see cref="DownloadTask"/> given during setup.
        /// </summary>
        /// <returns>Is successful.</returns>
        public async Task<Result<bool>> Start()
        {
            if (!_isSetup)
            {
                return Result.Fail(new Error("This plex download client has not been setup, run SetupAsync() first"));
            }

            Log.Debug($"Start downloading {DownloadTask.FileName} from {DownloadTask.DownloadUrl}");
            DownloadStartAt = DateTime.UtcNow;
            try
            {
                foreach (var downloadWorker in _downloadWorkers)
                {
                    var startResult = downloadWorker.Start();
                    if (startResult.IsFailed)
                    {
                        await ClearDownloadWorkers();
                        return startResult.LogError();
                    }
                }
            }
            catch (Exception e)
            {
                await ClearDownloadWorkers();
                return Result.Fail(new ExceptionalError(e)
                        .WithMessage($"Could not download {DownloadTask.FileName} from {DownloadTask.DownloadUrl}"))
                    .LogError();
            }

            return Result.Ok();
        }

        /// <summary>
        /// Setup this PlexDownloadClient to get ready for downloading.
        /// </summary>
        /// <param name="downloadWorkerTasks">Optional: If the <see cref="DownloadWorkerTask"/>s are already made then use those,
        /// otherwise they will be created.</param>
        /// <returns>Is successful.</returns>
        public async Task<Result<bool>> SetupAsync(List<DownloadWorkerTask> downloadWorkerTasks = null)
        {
            if (downloadWorkerTasks == null || !downloadWorkerTasks.Any())
            {
                // Create download worker tasks/segments/ranges
                var partSize = TotalBytesToReceive / Parts;
                var remainder = TotalBytesToReceive - partSize * Parts;
                downloadWorkerTasks = new List<DownloadWorkerTask>();
                for (int i = 0; i < Parts; i++)
                {
                    long start = partSize * i;
                    long end = start + partSize;
                    if (i == Parts - 1 && remainder > 0)
                    {
                        // Add the remainder to the last download range
                        end += remainder;
                    }

                    downloadWorkerTasks.Add(new DownloadWorkerTask(DownloadTask)
                    {
                        PartIndex = i + 1,
                        Url = DownloadTask.DownloadUrl,
                        StartByte = start,
                        EndByte = end,
                    });
                }

                // Verify bytes have been correctly divided
                var totalBytesInWorkers = downloadWorkerTasks.Sum(x => x.BytesRangeSize);
                if (totalBytesInWorkers != TotalBytesToReceive)
                {
                    Log.Error($"The bytes were incorrectly divided, expected {TotalBytesToReceive} but the sum was " +
                              $"{totalBytesInWorkers} with a difference of {TotalBytesToReceive - totalBytesInWorkers}");
                }

                // Send downloadWorkerTasks to the database and retrieve entries
                var result = await _mediator.Send(new AddDownloadWorkerTasksCommand(downloadWorkerTasks));
                if (result.IsFailed)
                {
                    return result.ToResult();
                }
            }

            var createResult = await CreateDownloadWorkers(DownloadTask.Id);
            if (createResult.IsFailed)
            {
                return createResult;
            }

            SetupSubscriptions();
            _isSetup = true;
            return Result.Ok(true);
        }

        private async Task<Result<bool>> CreateDownloadWorkers(int downloadTaskId)
        {
            var downloadTask = await _mediator.Send(new GetDownloadTaskByIdQuery(downloadTaskId, true, true));
            if (downloadTask.IsFailed)
            {
                return downloadTask.ToResult();
            }

            if (!downloadTask.Value.DownloadWorkerTasks.Any())
            {
                return Result.Fail($"Could not find any download worker tasks attached to download task {downloadTaskId}").LogError();
            }

            // Update the Download Task set in this client
            DownloadTask = downloadTask.Value;

            // Create workers
            foreach (var downloadWorkerTask in downloadTask.Value.DownloadWorkerTasks)
            {
                _downloadWorkers.Add(_downloadWorkerFactory(downloadWorkerTask));
            }

            return Result.Ok(true);
        }

        /// <summary>
        /// Immediately stops all and destroys the <see cref="DownloadWorker"/>s, will also removes any temporary files them.
        /// This will also remove any downloaded data.
        /// </summary>
        /// <returns>Is successful.</returns>
        public async Task<Result> Stop()
        {
            _downloadWorkers.AsParallel().ForAll(async downloadWorker =>
            {
                var stopResult = await downloadWorker.Stop();
                if (stopResult.IsFailed)
                {
                    stopResult.WithError(new Error(
                            $"Failed to stop downloadWorkerTask with id: {downloadWorker.Id} in PlexDownloadClient with id: {DownloadTask.Id}"))
                        .LogError();
                }
            });

            await ClearDownloadWorkers();

            _fileSystem.DeleteAllFilesFromDirectory(DownloadTask.DownloadPath);
            _fileSystem.DeleteDirectoryFromFilePath(DownloadTask.DownloadPath);

            return Result.Ok(true);
        }

        public async Task<Result> Pause()
        {
            if (DownloadStatus == DownloadStatus.Downloading)
            {
                _downloadWorkers.AsParallel().ForAll(async downloadWorker =>
                {
                    var pauseResult = await downloadWorker.Pause();
                    if (pauseResult.IsFailed)
                    {
                        pauseResult.WithError(new Error(
                                $"Failed to pause downloadWorkerTask with id: {downloadWorker.Id} in PlexDownloadClient with id: {DownloadTask.Id}"))
                            .LogError();
                    }
                });

                await ClearDownloadWorkers();
                return Result.Ok();
            }

            return Result.Ok();
        }

        #endregion

        #endregion

        #endregion
    }
}