using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Docker.DotNet.Tests
{
    [Collection("TestsCollection")]
    public class IContainerOperationsTests : IDisposable
    {
        private const string _imageName = "nats";
        private readonly DockerClient _dockerClient;
        private readonly DockerClientConfiguration _dockerConfiguration;
        private readonly ITestOutputHelper _output;
        private readonly string _imageId;

        private readonly string _repositoryName = Guid.NewGuid().ToString();

        public IContainerOperationsTests(ITestOutputHelper output)
        {
            _output = output;
            _dockerConfiguration = new DockerClientConfiguration();
            _dockerClient = _dockerConfiguration.CreateClient();

            using var cts = new CancellationTokenSource();

            _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = _imageName,
                    Tag = "latest"
                },
                authConfig: null,
                progress: new Progress<JSONMessage>((m) => _output.WriteLine(JsonConvert.SerializeObject(m))),
            cts.Token).GetAwaiter().GetResult();

            _imageId = _dockerClient.Images.ListImagesAsync(
                new ImagesListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["reference"] = new Dictionary<string, bool>
                        {
                            [$"{_imageName}:{"latest"}"] = true
                        }
                    }
                },
                cts.Token
            ).GetAwaiter().GetResult()[0].ID;

            _dockerClient.Images.TagImageAsync(
                _imageId,
                new ImageTagParameters
                {
                    RepositoryName = _repositoryName,
                    Force = true
                },
                cts.Token
            ).GetAwaiter().GetResult();

            _output.WriteLine($"Test image created -> ID '{_imageId}'");
        }

        [Fact]
        public async Task CreateContainerAsync_CreatesContainer()
        {
            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                },
                default);

            Assert.NotNull(createContainerResponse);
            Assert.NotEmpty(createContainerResponse.ID);

        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public async Task CreateContainerAsync_TimeoutExpires_Fails(int millisecondsTimeout)
        {
            using var dockerClientWithTimeout = _dockerConfiguration.CreateClient();

            dockerClientWithTimeout.DefaultTimeout = TimeSpan.FromMilliseconds(millisecondsTimeout);

            _output.WriteLine($"Time available for CreateContainer operation: {millisecondsTimeout} ms'");

            var timer = new Stopwatch();
            timer.Start();

            var createContainerTask = dockerClientWithTimeout.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                },
                default);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => createContainerTask);

            timer.Stop();
            _output.WriteLine($"CreateContainerOperation finished after {timer.ElapsedMilliseconds} ms");

            Assert.True(createContainerTask.IsCanceled);
            Assert.True(createContainerTask.IsCompleted);
        }

        [Fact]
        public async Task KillContainerAsync_ContainerExists_Succeeds()
        {
            using var cts = new CancellationTokenSource(delay: TimeSpan.FromMinutes(10));

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = _imageId
                },
                cts.Token);

            var executeContainerResponse = await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                cts.Token);

            _output.WriteLine(JsonConvert.SerializeObject(
                await _dockerClient.Containers.InspectContainerAsync(
                    createContainerResponse.ID,
                    cts.Token
                    )
                ));

            await _dockerClient.Containers.KillContainerAsync(
                createContainerResponse.ID,
                new ContainerKillParameters(),
                cts.Token);

            var containerInspectResponse = await _dockerClient.Containers.InspectContainerAsync(
                createContainerResponse.ID,
                cts.Token);

            await _dockerClient.Containers.RemoveContainerAsync(
                createContainerResponse.ID,
                new ContainerRemoveParameters
                {
                    Force = true
                },
                cts.Token);

            Assert.Equal("exited", containerInspectResponse.State.Status);

        }

        [Fact]
        public async Task ListContainersAsync_ContainerExists_Succeeds()
        {
            using var cts = new CancellationTokenSource();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters()
            {
                Image = _repositoryName,
                Name = Guid.NewGuid().ToString()
            },
            cts.Token);

            bool startContainerResult = await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                cts.Token
            );

            IList<ContainerListResponse> containerList = await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["ancestor"] = new Dictionary<string, bool>
                        {
                            [_repositoryName] = true
                        }
                    }
                },
                cts.Token
            );

            Assert.NotNull(containerList);
            Assert.NotEmpty(containerList);
        }

        [Fact]
        public async Task ListProcessesAsync_StateUnderTest_ExpectedBehavior()
        {
            using var cts = new CancellationTokenSource();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters()
            {
                Image = _repositoryName,
                Name = Guid.NewGuid().ToString()
            },
            cts.Token);

            bool startContainerResult = await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                cts.Token
            );

            IList<ContainerListResponse> containerList = await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["ancestor"] = new Dictionary<string, bool>
                        {
                            [_repositoryName] = true
                        }
                    }
                },
                cts.Token
            );

            var containerProcessesResponse = await _dockerClient.Containers.ListProcessesAsync(
                containerList[0].ID,
                new ContainerListProcessesParameters(),
                cts.Token
            );

            _output.WriteLine($"Title  '{containerProcessesResponse.Titles[0]}' - '{containerProcessesResponse.Titles[1]}' - '{containerProcessesResponse.Titles[2]}' - '{containerProcessesResponse.Titles[3]}'");

            foreach (var processes in containerProcessesResponse.Processes)
            {
                _output.WriteLine($"Process '{processes[0]}' - ''{processes[1]}' - '{processes[2]}' - '{processes[3]}'");
            }

            Assert.NotNull(containerProcessesResponse);
            Assert.NotEmpty(containerProcessesResponse.Processes);
        }


        [Fact]
        public async Task RemoveContainerAsync_ContainerExists_Succeedes()
        {
            using var cts = new CancellationTokenSource();

            IList<ContainerListResponse> initialContainerList = await _dockerClient.Containers.ListContainersAsync(
                 new ContainersListParameters
                 {
                     Filters = new Dictionary<string, IDictionary<string, bool>>
                     {
                         ["ancestor"] = new Dictionary<string, bool>
                         {
                             [_repositoryName] = true
                         }
                     },
                     All = true
                 },
                 cts.Token
            );

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _repositoryName,
                    Name = Guid.NewGuid().ToString()
                },
                cts.Token
            );

            IList<ContainerListResponse> updatedContainerList = await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["ancestor"] = new Dictionary<string, bool>
                        {
                            [_repositoryName] = true
                        }
                    },
                    All = true
                },
                cts.Token
            );

            await _dockerClient.Containers.RemoveContainerAsync(
                createContainerResponse.ID,
                new ContainerRemoveParameters
                {
                    Force = true
                },
                cts.Token
            );

            IList<ContainerListResponse> finalContainerList = await _dockerClient.Containers.ListContainersAsync(
                 new ContainersListParameters
                 {
                     Filters = new Dictionary<string, IDictionary<string, bool>>
                     {
                         ["ancestor"] = new Dictionary<string, bool>
                         {
                             [_repositoryName] = true
                         }
                     },
                     All = true
                 },
                 cts.Token
            );

            Assert.True(updatedContainerList.Count - initialContainerList.Count == 1);
            Assert.True(initialContainerList.Count == finalContainerList.Count);
        }

        [Fact]
        public async Task StartContainerAsync_ContainerStopped_True()
        {
            using var cts = new CancellationTokenSource();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _repositoryName,
                    Name = Guid.NewGuid().ToString()
                },
                cts.Token
            );

            bool startContainerResult = await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                cts.Token
            );

            Assert.True(startContainerResult);
        }

        [Fact]
        public async Task StartContainerAsync_ContainerRunning_False()
        {
            using var cts = new CancellationTokenSource();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _repositoryName,
                    Name = Guid.NewGuid().ToString()
                },
                cts.Token
            );

            await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                cts.Token
            );

            var startContainerResult = await _dockerClient.Containers.StartContainerAsync(
                            createContainerResponse.ID,
                            new ContainerStartParameters(),
                            cts.Token
                        );

            Assert.False(startContainerResult);
        }

        [Fact]
        public async Task StartContainerAsync_ContainerNotExists_ThrowsException()
        {
            using var cts = new CancellationTokenSource();

            Task startContainerTask = _dockerClient.Containers.StartContainerAsync(
                Guid.NewGuid().ToString(),
                new ContainerStartParameters(),
                cts.Token
            );

            await Assert.ThrowsAnyAsync<DockerContainerNotFoundException>(() => startContainerTask);
        }


        [Fact]
        public async Task WaitContainerAsync_TokenIsCancelled_OperationCancelledException()
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            using var cts = new CancellationTokenSource(delay: TimeSpan.FromMinutes(10));

            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = _imageName,
                    Tag = "latest"
                },
                authConfig: null,
                progress: new Progress<JSONMessage>((m) => _output.WriteLine(JsonConvert.SerializeObject(m))),
                cts.Token);

            var imageListResponse = await _dockerClient.Images.ListImagesAsync(
                new ImagesListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["reference"] = new Dictionary<string, bool>
                        {
                            [_imageName] = true
                        }
                    }
                },
                cts.Token
            );

            _output.WriteLine($"ImageListResponse: {JsonConvert.SerializeObject(imageListResponse)}");

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters()
            {
                Image = _imageName,
                Name = Guid.NewGuid().ToString(),
            },
            cts.Token);

            _output.WriteLine($"CreateContainerResponse: '{JsonConvert.SerializeObject(createContainerResponse)}'");

            var startContainerTask = _dockerClient.Containers.StartContainerAsync(createContainerResponse.ID, new ContainerStartParameters(), cts.Token);
            var startContainerResult = await startContainerTask;

            _output.WriteLine($"StartContainerAsync: {stopWatch.Elapsed} ms, Task.IsCompleted: '{startContainerTask.IsCompleted}', Task.IsCompletedSuccessfully: '{startContainerTask.IsCompletedSuccessfully}', Task.IsCanceled: '{startContainerTask.IsCanceled}', StartContainerResult: '{JsonConvert.SerializeObject(startContainerResult)}'");

            _output.WriteLine("Starting 500 ms timeout to cancel operation.");
            cts.CancelAfter(500);

            // Will wait forever if cancelation not working
            var waitContainerTask = _dockerClient.Containers.WaitContainerAsync(createContainerResponse.ID, cts.Token);

            try
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitContainerTask);
            }
            catch (OperationCanceledException ex)
            {
                _output.WriteLine($"WaitContainerTask was cancelled after {stopWatch.ElapsedMilliseconds} ms -> '{ex.Message}'");
            }

            _output.WriteLine($"WaitContainerAsync: {stopWatch.Elapsed} elapsed");
            _output.WriteLine($"Task.IsCanceled: '{JsonConvert.SerializeObject(waitContainerTask.IsCanceled)}'");
            _output.WriteLine($"Task.IsCompleted: '{JsonConvert.SerializeObject(waitContainerTask.IsCompleted)}'");

            _ = await _dockerClient.Containers.StopContainerAsync(
                createContainerResponse.ID,
                new ContainerStopParameters
                {
                    WaitBeforeKillSeconds = 0
                },
                default);

            await _dockerClient.Containers.RemoveContainerAsync(
                createContainerResponse.ID,
                new ContainerRemoveParameters()
                {
                    Force = true
                },
                default);
        }


        public void Dispose()
        {
            var containerList = _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["ancestor"] = new Dictionary<string, bool>
                        {
                            [_repositoryName] = true
                        }
                    }
                }
            ).GetAwaiter().GetResult();

            foreach (ContainerListResponse container in containerList)
            {
                _dockerClient.Containers.RemoveContainerAsync(
                container.ID,
                new ContainerRemoveParameters
                {
                    Force = true
                },
                default).GetAwaiter().GetResult();
            }

            _dockerClient.Images.DeleteImageAsync(
                _repositoryName,
                new ImageDeleteParameters
                {
                    Force = true
                },
                default).GetAwaiter().GetResult();

            _dockerClient.Dispose();
            _dockerConfiguration.Dispose();
        }
    }
}
