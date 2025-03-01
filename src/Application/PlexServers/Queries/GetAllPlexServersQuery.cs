﻿using System.Collections.Generic;
using FluentResults;
using MediatR;
using PlexRipper.Domain;

namespace PlexRipper.Application.PlexServers
{
    public class GetAllPlexServersQuery : IRequest<Result<List<PlexServer>>>
    {
        /// <summary>
        /// Retrieves all the  <see cref="PlexServer">PlexServers</see> currently in the database.
        /// </summary>
        /// <param name="includeLibraries">Include <see cref="PlexLibrary"/> relationship.</param>
        /// <param name="plexAccountId">Only retrieve <see cref="PlexServer">PlexServers</see> accessible by this <see cref="PlexAccount"/>.</param>
        public GetAllPlexServersQuery(bool includeLibraries = false, int plexAccountId = 0)
        {
            IncludeLibraries = includeLibraries;
            PlexAccountId = plexAccountId;
        }

        public bool IncludeLibraries { get; }

        public int PlexAccountId { get; }
    }
}