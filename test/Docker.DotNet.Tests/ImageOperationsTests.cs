using System;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Docker.DotNet.Tests
{

    [Collection("TestsCollection")]
    public class IImageOperationsTests : IDisposable
    {
        private const string _imageName = "nats";
        private readonly ITestOutputHelper _output;
        private readonly DockerClientConfiguration _dockerConfiguration;
        private readonly DockerClient _dockerClient;

        public IImageOperationsTests(ITestOutputHelper output)
        {
            _output = output;
            _dockerConfiguration = new DockerClientConfiguration();
            _dockerClient = _dockerConfiguration.CreateClient();
        }
        [Fact]
        public async Task CreateImageAsync_TaskCancelled_ThowsOperationCancelledException()
        {

            using var cts = new CancellationTokenSource();

            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = _imageName,
                    Tag = "latest"
                },
                authConfig: null,
                progress: new Progress<JSONMessage>((m) => _output.WriteLine(JsonConvert.SerializeObject(m))),
                cts.Token
            );

            var tag = Guid.NewGuid().ToString();
            var repositoryName = Guid.NewGuid().ToString();

            await _dockerClient.Images.TagImageAsync(
                _imageName,
                new ImageTagParameters
                {
                    RepositoryName = repositoryName,
                    Tag = tag,
                    Force = true
                },
                cts.Token
            );

            var createContainerTask = _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = $"{repositoryName}:{tag}"
                },
                null,
                new Progress<JSONMessage>((message) => _output.WriteLine(JsonConvert.SerializeObject(message))),
                cts.Token);

            cts.CancelAfter(5);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => createContainerTask);

            Assert.True(createContainerTask.IsCanceled);

            await _dockerClient.Images.DeleteImageAsync(
                $"{repositoryName}:{tag}",
                new ImageDeleteParameters
                {
                    Force = true
                },
                default
            );
        }


        public void Dispose()
        {
            _dockerClient.Dispose();
            _dockerConfiguration.Dispose();
        }
    }
}
