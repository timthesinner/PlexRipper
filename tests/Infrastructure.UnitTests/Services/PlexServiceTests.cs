﻿using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Threading.Tasks;

namespace Infrastructure.UnitTests.Services
{
    public class PlexServiceTests
    {

        [SetUp]
        public void Setup()
        {
            BaseDependanciesTest.Setup();
        }

        [Test]
        public async Task ShouldReturnValidApiToken()
        {
            // Arrange
            var plexService = BaseServiceTest.GetPlexService();
            var accountService = BaseServiceTest.GetAccountService();
            var credentials = BaseServiceTest.GetCredentials();

            //Act 
            var account = await accountService.AddAccountAsync(credentials.Username, credentials.Password);
            var result = await accountService.ValidateAccountAsync(account);
            string authToken = await plexService.GetPlexToken(account);

            //Assert
            Assert.IsNotNull(result);
            Assert.IsNotEmpty(authToken);
        }


        [Test]
        public async Task ShouldReturnListOfServers()
        {
            // Arrange
            var plexService = BaseServiceTest.GetPlexService();
            var accountService = BaseServiceTest.GetAccountService();
            var credentials = BaseServiceTest.GetCredentials();
            //Act 
            var account = await accountService.AddAccountAsync(credentials.Username, credentials.Password);
            var result = await accountService.ValidateAccountAsync(account);
            var serverList = await plexService.GetServers(account, true);

            //Assert
            Assert.IsNotNull(result);
            Assert.IsNotEmpty(serverList);
        }


        [Test]
        public async Task ShouldReturnPlexLibrary()
        {

            var plexService = BaseServiceTest.GetPlexService();
            var accountService = BaseServiceTest.GetAccountService();
            var credentials = BaseServiceTest.GetCredentials();

            var account = await accountService.GetAccountAsync(credentials.Username);

            var serverList = await plexService.GetServers(account);
            var library = await plexService.GetLibrary(serverList[0]);

            Assert.IsNotNull(library);
        }




        [Test]
        public void ShouldLogDebugToUnitTestConsole()
        {
            var logger = BaseDependanciesTest.GetLogger<object>();
            logger.LogDebug("This is a debug string");
            logger.LogInformation("This is an information string");
            logger.LogError("This is a error string");
            logger.LogCritical("This is a critical string");
        }
    }
}
