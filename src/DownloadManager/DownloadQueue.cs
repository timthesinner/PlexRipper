﻿using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using FluentResultExtensions.lib;
using Logging;
using PlexRipper.Application.Common;
using PlexRipper.Domain;

namespace PlexRipper.DownloadManager
{
    /// <summary>
    /// The DownloadQueue is responsible for deciding which downloadTask is handled by the <see cref="DownloadManager"/>.
    /// </summary>
    public class DownloadQueue : IDownloadQueue
    {
        public Subject<DownloadTask> UpdateDownloadTask { get; } = new();

        public Subject<int> StartDownloadTask { get; } = new();

        public void ExecuteDownloadQueue(List<PlexServer> plexServers)
        {
            if (!plexServers.Any())
            {
                Log.Information("There are no PlexServers with DownloadTasks");
                return;
            }

            Log.Information($"Starting the check of {plexServers.Count} PlexServers.");
            foreach (var plexServer in plexServers)
            {
                var downloadTasks = plexServer.PlexLibraries.SelectMany(x => x.DownloadTasks).ToList();

                // Set all initialized to Queued
                foreach (var downloadTask in downloadTasks)
                {
                    if (downloadTask.DownloadStatus == DownloadStatus.Initialized)
                    {
                        downloadTask.DownloadStatus = DownloadStatus.Queued;
                        UpdateDownloadTask.OnNext(downloadTask);
                    }
                }

                // Check if this server is already downloading a downloadTask
                if (downloadTasks.Any(x => x.DownloadStatus == DownloadStatus.Downloading))
                {
                    Log.Warning($"PlexServer: {plexServer.Name} already has a download which is in currently downloading");
                    continue;
                }

                var queuedDownloadTask = downloadTasks.FirstOrDefault(x => x.DownloadStatus == DownloadStatus.Queued);
                if (queuedDownloadTask != null)
                {
                    Log.Debug(
                        $"Starting the next Queued downloadTask with id {queuedDownloadTask.Id} - {queuedDownloadTask.Title} for server {plexServer.Name}");
                   // UpdateDownloadTask.OnNext(queuedDownloadTask);
                    StartDownloadTask.OnNext(queuedDownloadTask.Id);
                    return;
                }

                Log.Information($"There are no available downloadTasks remaining for PlexServer: {plexServer.Name}");
            }
        }
    }
}