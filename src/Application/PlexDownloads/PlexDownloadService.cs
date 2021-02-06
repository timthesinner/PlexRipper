﻿#region

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentResults;
using MediatR;
using PlexRipper.Application.Common;
using PlexRipper.Application.Common.DTO.WebApi;
using PlexRipper.Application.PlexMovies;
using PlexRipper.Application.PlexTvShows;
using PlexRipper.Domain;

#endregion

namespace PlexRipper.Application.PlexDownloads
{
    public class PlexDownloadService : IPlexDownloadService
    {
        #region Fields

        private readonly IDownloadManager _downloadManager;

        private readonly IFolderPathService _folderPathService;

        private readonly INotificationsService _notificationsService;

        private readonly IMediator _mediator;

        private readonly IPlexAuthenticationService _plexAuthenticationService;

        private readonly ISignalRService _signalRService;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="PlexDownloadService"/> class.
        /// </summary>
        /// <param name="mediator"></param>
        /// <param name="downloadManager"></param>
        /// <param name="plexAuthenticationService"></param>
        /// <param name="fileSystem"></param>
        /// <param name="plexApiService"></param>
        /// <param name="signalRService"></param>
        /// <param name="folderPathService"></param>
        /// <param name="notificationsService"></param>
        public PlexDownloadService(
            IMediator mediator,
            IDownloadManager downloadManager,
            IPlexAuthenticationService plexAuthenticationService,
            ISignalRService signalRService,
            IFolderPathService folderPathService,
            INotificationsService notificationsService)
        {
            _mediator = mediator;
            _downloadManager = downloadManager;
            _plexAuthenticationService = plexAuthenticationService;
            _signalRService = signalRService;
            _folderPathService = folderPathService;
            _notificationsService = notificationsService;
        }

        #endregion

        #region Methods

        #region Public

        public Task<Result<List<PlexServer>>> GetDownloadTasksInServerAsync()
        {
            return _mediator.Send(new GetAllDownloadTasksInPlexServersQuery(true));
        }

        public async Task<Result<List<DownloadTaskDTO>>> GetDownloadTasksAsync()
        {
            var result = await _mediator.Send(new GetAllDownloadTasksQuery());
            if (result.IsFailed)
            {
                return result.ToResult();
            }

            var downloadTasks = result.Value;

            var tvShowDownloadTasks = new List<DownloadTaskDTO>();
            foreach (var downloadTask in downloadTasks.OrderBy(x => x.Id))
            {
                if (downloadTask.MediaType == PlexMediaType.Episode)
                {
                    var tvShowDownloadTask =
                        tvShowDownloadTasks.Find(x => x.Key == downloadTask.MetaData.TvShowKey && x.PlexLibraryId == downloadTask.PlexLibraryId);
                    if (tvShowDownloadTask is null)
                    {
                        tvShowDownloadTask = new DownloadTaskDTO
                        {
                            Key = downloadTask.MetaData.TvShowKey,
                            Title = downloadTask.TitleTvShow,
                            FullTitle = downloadTask.TitleTvShow,
                            MediaType = PlexMediaType.TvShow,
                            PlexServerId = downloadTask.PlexServerId,
                            PlexLibraryId = downloadTask.PlexLibraryId,
                        };
                        tvShowDownloadTasks.Add(tvShowDownloadTask);
                    }

                    var tvShowSeasonDownloadTask = tvShowDownloadTasks.SelectMany(x => x.Children).ToList()
                        .Find(x => x.Key == downloadTask.MetaData.TvShowSeasonKey && x.PlexLibraryId == downloadTask.PlexLibraryId);
                    if (tvShowSeasonDownloadTask is null)
                    {
                        tvShowSeasonDownloadTask = new DownloadTaskDTO
                        {
                            Key = downloadTask.MetaData.TvShowSeasonKey,
                            Title = downloadTask.TitleTvShowSeason,
                            FullTitle = $"{tvShowDownloadTask.Title}/{downloadTask.TitleTvShowSeason}",
                            MediaType = PlexMediaType.Season,
                            PlexServerId = downloadTask.PlexServerId,
                            PlexLibraryId = downloadTask.PlexLibraryId,
                        };
                        tvShowDownloadTask.Children.Add(tvShowSeasonDownloadTask);
                    }

                    tvShowSeasonDownloadTask.Children.Add(new DownloadTaskDTO
                    {
                        Id = downloadTask.Id,
                        Key = downloadTask.MetaData.TvShowEpisodeKey,
                        Title = downloadTask.TitleTvShowEpisode,
                        FullTitle = $"{tvShowDownloadTask.Title}/{tvShowSeasonDownloadTask.Title}/{downloadTask.TitleTvShowEpisode}",
                        MediaType = PlexMediaType.Episode,
                        DataReceived = downloadTask.DataReceived,
                        DataTotal = downloadTask.DataTotal,
                        PlexServerId = downloadTask.PlexServerId,
                        PlexLibraryId = downloadTask.PlexLibraryId,
                        Status = downloadTask.DownloadStatus,
                        DestinationPath = downloadTask.DestinationPath,
                        DownloadPath = downloadTask.DownloadPath,
                        DownloadUrl = downloadTask.DownloadUrl,
                        Percentage = downloadTask.Percentage,
                        FileName = downloadTask.FileName,
                    });
                }
            }

            tvShowDownloadTasks.ForEach(tvShow =>
            {
                tvShow.Children.ForEach(season =>
                {
                    season.DataTotal = season.Children.Sum(x => x.DataTotal);
                    season.DataReceived = season.Children.Sum(x => x.DataReceived);
                    season.DataReceived = season.Children.Sum(x => x.DataReceived);
                    season.Percentage = decimal.Round(season.Children.Average(x => x.Percentage), 2);
                });

                tvShow.DataTotal = tvShow.Children.Sum(x => x.DataTotal);
                tvShow.DataReceived = tvShow.Children.Sum(x => x.DataReceived);
                tvShow.DataReceived = tvShow.Children.Sum(x => x.DataReceived);
                tvShow.Percentage = decimal.Round(tvShow.Children.Average(x => x.Percentage), 2);
            });
            return Result.Ok(tvShowDownloadTasks);
        }

        public Task<string> GetPlexTokenAsync(PlexAccount plexAccount)
        {
            return _plexAuthenticationService.GetPlexApiTokenAsync(plexAccount);
        }

        #region Commands

        public async Task<Result> DownloadMediaAsync(List<DownloadMediaDTO> downloadMedias)
        {
            int mediaCount = downloadMedias.Select(x => x.MediaIds.Count).Sum();
            await _signalRService.SendDownloadTaskCreationProgressUpdate(1, mediaCount);
            int count = 0;
            for (int i = 0; i < downloadMedias.Count; i++)
            {
                var downloadMedia = downloadMedias[i];
                var result = await DownloadMediaAsync(downloadMedia.MediaIds, downloadMedia.Type, downloadMedia.LibraryId);
                if (result.IsFailed)
                {
                    await _notificationsService.SendResult(result);
                }

                count += downloadMedia.MediaIds.Count;
                await _signalRService.SendDownloadTaskCreationProgressUpdate(count, mediaCount);
            }

            await _signalRService.SendDownloadTaskCreationProgressUpdate(mediaCount, mediaCount);

            return Result.Ok();
        }

        public async Task<Result> DownloadMediaAsync(List<int> mediaIds, PlexMediaType type, int libraryId, int plexAccountId = 0)
        {
            var result = await _folderPathService.CheckIfFolderPathsAreValid();
            if (result.IsFailed)
            {
                return result;
            }

            switch (type)
            {
                case PlexMediaType.Movie:
                    return await DownloadMovieAsync(mediaIds, plexAccountId);
                case PlexMediaType.TvShow:
                    return await DownloadTvShowAsync(mediaIds, plexAccountId);
                case PlexMediaType.Season:
                    return await DownloadTvShowSeasonAsync(mediaIds, plexAccountId);
                case PlexMediaType.Episode:
                    return await DownloadTvShowEpisodeAsync(mediaIds, plexAccountId);
                case PlexMediaType.Music:
                case PlexMediaType.Album:
                    return Result.Fail("PlexMediaType was Music or Album, this is not yet supported").LogWarning();
                case PlexMediaType.None:
                    return Result.Fail("PlexMediaType was none in DownloadMediaAsync").LogWarning();
                case PlexMediaType.Unknown:
                    return Result.Fail("PlexMediaType was Unknown in DownloadMediaAsync").LogWarning();
                default:
                    return Result.Fail($"PlexMediaType defaulted with value {type.ToString()} in DownloadMediaAsync").LogWarning();
            }
        }

        public async Task<Result<bool>> DeleteDownloadTasksAsync(IEnumerable<int> downloadTaskIds)
        {
            return await _downloadManager.DeleteDownloadClients(downloadTaskIds);
        }

        public async Task<Result<bool>> RestartDownloadTask(int downloadTaskId)
        {
            if (downloadTaskId <= 0) return ResultExtensions.IsInvalidId(nameof(downloadTaskId), downloadTaskId).LogWarning();

            return await _downloadManager.RestartDownloadAsync(downloadTaskId);
        }

        public async Task<Result> StopDownloadTask(List<int> downloadTaskIds = null)
        {
            return await _downloadManager.StopDownload(downloadTaskIds);
        }

        public async Task<Result<bool>> StartDownloadTask(int downloadTaskId)
        {
            if (downloadTaskId <= 0) return ResultExtensions.IsInvalidId(nameof(downloadTaskId), downloadTaskId).LogWarning();

            return await _downloadManager.StartDownload(downloadTaskId);
        }

        public async Task<Result> PauseDownloadTask(int downloadTaskId)
        {
            if (downloadTaskId <= 0) return ResultExtensions.IsInvalidId(nameof(downloadTaskId), downloadTaskId).LogWarning();

            return await _downloadManager.PauseDownload(downloadTaskId);
        }

        public Task<Result<bool>> ClearCompleted(List<int> downloadTaskIds)
        {
            return _downloadManager.ClearCompletedAsync(downloadTaskIds);
        }

        #endregion

        #endregion

        #region Private

        /// <summary>
        /// Creates <see cref="DownloadTask"/>s from a <see cref="PlexMovie"/> and send it to the <see cref="IDownloadManager"/>.
        /// </summary>
        /// <param name="plexAccountId">The id of the <see cref="PlexAccount"/> to authenticate with.</param>
        /// <param name="plexMovieIds">The ids of the <see cref="PlexMovie"/> to create <see cref="DownloadTask"/>s from.</param>
        /// <returns>The created <see cref="DownloadTask"/>.</returns>
        private async Task<Result<bool>> DownloadMovieAsync(List<int> plexMovieIds, int plexAccountId = 0)
        {
            Log.Debug($"Creating {plexMovieIds.Count} movie download tasks.");
            var plexMoviesResult = await _mediator.Send(new GetMultiplePlexMoviesByIdsQuery(plexMovieIds, true, true));
            if (plexMoviesResult.IsFailed) return plexMoviesResult.ToResult<bool>();

            var downloadTasks = new List<DownloadTask>();
            foreach (var plexMovie in plexMoviesResult.Value)
            {
                downloadTasks.AddRange(plexMovie.CreateDownloadTasks());

                Log.Debug($"Created download task(s) for movie: {plexMovie.Title}");
            }

            return await FinalizeDownloadTasks(downloadTasks, plexAccountId);
        }

        private async Task<Result<bool>> DownloadTvShowAsync(List<int> plexTvShowIds, int plexAccountId = 0)
        {
            Log.Debug($"Creating download tasks for TvShow with id: {plexTvShowIds}");

            var plexTvShows = await _mediator.Send(new GetMultiplePlexTvShowsByIdsWithEpisodesQuery(plexTvShowIds, true, true, true));
            if (plexTvShows.IsFailed) return plexTvShows.ToResult();

            // Parse all contained episodes to DownloadTasks
            var downloadTasks = new List<DownloadTask>();
            foreach (var plexTvShow in plexTvShows.Value)
            {
                var tvShowDownloadTasks = plexTvShow.CreateDownloadTasks();
                foreach (DownloadTask downloadTask in tvShowDownloadTasks)
                {
                    downloadTask.PlexLibrary = plexTvShow.PlexLibrary;
                    downloadTask.PlexServer = plexTvShow.PlexServer;
                }

                downloadTasks.AddRange(tvShowDownloadTasks);
                Log.Debug($"Created download task(s) for tvShow: {plexTvShow.Title}");
            }

            return await FinalizeDownloadTasks(downloadTasks, plexAccountId);
        }

        private async Task<Result<bool>> DownloadTvShowSeasonAsync(List<int> plexTvShowSeasonIds, int plexAccountId = 0)
        {
            Log.Debug($"Creating download request for TvShow season with id: {plexTvShowSeasonIds}");

            var plexTvShowSeasonResult =
                await _mediator.Send(new GetMultiplePlexTvShowSeasonsByIdsWithEpisodesQuery(plexTvShowSeasonIds, true, true, true));
            if (plexTvShowSeasonResult.IsFailed)
                return plexTvShowSeasonResult.ToResult();

            var downloadTasks = new List<DownloadTask>();
            foreach (var plexTvShowSeason in plexTvShowSeasonResult.Value)
            {
                var seasonDownloadTasks = plexTvShowSeason.CreateDownloadTasks();
                foreach (DownloadTask downloadTask in seasonDownloadTasks)
                {
                    downloadTask.PlexLibrary = plexTvShowSeason.PlexLibrary;
                    downloadTask.PlexServer = plexTvShowSeason.PlexServer;
                }

                downloadTasks.AddRange(seasonDownloadTasks);
                Log.Debug($"Created download task(s) for tvShowSeasons: {plexTvShowSeason.Title}");
            }

            return await FinalizeDownloadTasks(downloadTasks, plexAccountId);
        }

        private async Task<Result<bool>> DownloadTvShowEpisodeAsync(List<int> plexTvShowEpisodeId, int plexAccountId = 0)
        {
            Log.Debug($"Creating download request for TvShow episode with id: {plexTvShowEpisodeId}");

            var plexTvShowEpisodeResult = await _mediator.Send(new GetMultiplePlexTvShowEpisodesByIdQuery(plexTvShowEpisodeId, true, true, true));
            if (plexTvShowEpisodeResult.IsFailed)
                return plexTvShowEpisodeResult.ToResult();

            var downloadTasks = new List<DownloadTask>();
            foreach (var plexTvShowSeason in plexTvShowEpisodeResult.Value)
            {
                downloadTasks.AddRange(plexTvShowSeason.CreateDownloadTasks());

                Log.Debug($"Created download task(s) for tvShowEpisodes: {plexTvShowSeason.Title}");
            }

            return await FinalizeDownloadTasks(downloadTasks, plexAccountId);
        }

        private async Task<Result<bool>> FinalizeDownloadTasks(List<DownloadTask> downloadTasks, int plexAccountId = 0)
        {
            if (!downloadTasks.Any())
                return ResultExtensions.IsEmpty(nameof(downloadTasks)).LogWarning();

            // Get the download folder
            var downloadFolder = await _folderPathService.GetDownloadFolderAsync();
            if (downloadFolder.IsFailed)
                return downloadFolder.ToResult();

            // Get the destination folder
            var destinationFolder = await _folderPathService.GetDestinationFolderByMediaType(downloadTasks.First().MediaType);
            if (destinationFolder.IsFailed)
                return destinationFolder.ToResult();

            // Get plex server access token
            var serverToken = await _plexAuthenticationService.GetPlexServerTokenAsync(downloadTasks.First().PlexServerId, plexAccountId);
            if (serverToken.IsFailed)
                return serverToken.ToResult();

            foreach (var downloadTask in downloadTasks)
            {
                downloadTask.DownloadFolderId = downloadFolder.Value.Id;
                downloadTask.DownloadFolder = downloadFolder.Value;
                downloadTask.DestinationFolderId = destinationFolder.Value.Id;
                downloadTask.DestinationFolder = destinationFolder.Value;
                downloadTask.ServerToken = serverToken.Value;
            }

            return await _downloadManager.AddToDownloadQueueAsync(downloadTasks);
        }

        private Result<DownloadTask> PrioritizeDownloadTask(DownloadTask downloadTask)
        {
            // TODO This is intended to change the order of downloads, not finished
            downloadTask.Priority = DataFormat.GetPriority();
            return Result.Ok(downloadTask);
        }

        private Result<List<DownloadTask>> PrioritizeDownloadTasks(List<DownloadTask> downloadTasks)
        {
            // TODO This is intended to change the order of downloads, not finished
            var priorities = DataFormat.GetPriority(downloadTasks.Count);
            for (int i = 0; i < downloadTasks.Count; i++)
            {
                downloadTasks[i].Priority = priorities[i];
            }

            return Result.Ok(downloadTasks);
        }

        #endregion

        #endregion
    }
}