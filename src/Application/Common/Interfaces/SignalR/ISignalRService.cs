﻿using System.Threading.Tasks;
using PlexRipper.Application.Common.WebApi;
using PlexRipper.Domain;

namespace PlexRipper.Application.Common
{
    public interface ISignalRService
    {
        void SendLibraryProgressUpdate(LibraryProgress libraryProgress);

        void SendLibraryProgressUpdate(int id, int received, int total, bool isRefreshing = true);

        Task SendDownloadTaskCreationProgressUpdate(int current, int total);

        void SendDownloadTaskUpdate(DownloadTask downloadTask);

        /// <summary>
        /// Sends a <see cref="FileMergeProgress"/> object to the SignalR client in the front-end.
        /// </summary>
        /// <param name="fileMergeProgress">The <see cref="FileMergeProgress"/> object to send.</param>
        void SendFileMergeProgressUpdate(FileMergeProgress fileMergeProgress);

        Task SendNotification(Notification notification);

        Task SendServerInspectStatusProgress(InspectServerProgress progress);

        void SendServerSyncProgressUpdate(SyncServerProgress syncServerProgress);
    }
}