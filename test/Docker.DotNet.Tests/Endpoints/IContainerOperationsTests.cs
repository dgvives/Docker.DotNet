using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Docker.DotNet.Tests.Endpoints
{
    [CollectionDefinition("TestCollection", DisableParallelization = true)]
    public class IContainerOperationsTests : IDisposable
    {
        private const string ImageName = "nats";
        private readonly DockerClient _dockerClient;
        private readonly DockerClientConfiguration _dockerConfiguration;
        private readonly ITestOutputHelper _output;
        private readonly string _testImageTag;

        public IContainerOperationsTests(ITestOutputHelper output)
        {
            _output = output;
            _dockerConfiguration = new DockerClientConfiguration();
            _dockerClient = _dockerConfiguration.CreateClient();
            _testImageTag = Guid.NewGuid().ToString();

            var cts = new CancellationTokenSource();

            _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = IContainerOperationsTests.ImageName,
                    Tag = "latest"
                },
                authConfig: null,
                progress: new Progress<JSONMessage>((m) => _output.WriteLine(JsonConvert.SerializeObject(m))),
                cts.Token).GetAwaiter().GetResult();

            var imageListResponse = _dockerClient.Images.ListImagesAsync(
                new ImagesListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["reference"] = new Dictionary<string, bool>
                        {
                            [ImageName] = true
                        }
                    }
                },
                cts.Token
            ).GetAwaiter().GetResult();

            _output.WriteLine($"ImageListResponse: {JsonConvert.SerializeObject(imageListResponse)}");

            _dockerClient.Images.TagImageAsync(
                IContainerOperationsTests.ImageName,
                new ImageTagParameters
                {
                    RepositoryName = IContainerOperationsTests.ImageName,
                    Tag = _testImageTag
                },
                cts.Token).GetAwaiter().GetResult();
        }

        [Fact]
        public async Task CreateContainerAsync_CreatesContainer()
        {
            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = $"{ImageName}:{_testImageTag}",
                    Name = Guid.NewGuid().ToString(),
                },
                default);

            Assert.NotNull(createContainerResponse);
            Assert.NotEmpty(createContainerResponse.ID);

        }

        [Fact]
        public async Task ExportContainerAsync_ExistingContainer_ReadBytes()
        {
            var cts = new CancellationTokenSource();
            var containerListResponse = await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["ancestor"] = new Dictionary<string, bool>
                        {
                            [$"{IContainerOperationsTests.ImageName}:{_testImageTag}"] = true
                        }
                    }
                },
                cts.Token
            );

            Assert.NotNull(containerListResponse);
            Assert.NotEmpty(containerListResponse);

            Stream containerExportStream = await _dockerClient.Containers.ExportContainerAsync(
                containerListResponse[0].ID,
                cts.Token
            );

            StreamReader containerExportReader = new StreamReader(containerExportStream);

            var buffer = new Memory<char>(new char[128]);

            var containerExportByteCount = await containerExportReader.ReadAsync(buffer, cts.Token);

            cts.Cancel();

            Assert.True(containerExportByteCount > 0);

        }

        [Fact]
        public async Task ExtractArchiveToContainerAsync_StateUnderTest_ExpectedBehavior()
        {
            var cts = new CancellationTokenSource();
            var containerListResponse = await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["ancestor"] = new Dictionary<string, bool>
                        {
                            [$"{IContainerOperationsTests.ImageName}:latest"] = true
                        }
                    }
                },
                cts.Token
            );

            Assert.NotNull(containerListResponse);
            Assert.NotEmpty(containerListResponse);

            var buffer = new byte[128];

            using var fileStream = new MemoryStream(buffer);
            var extractArchiveTask = _dockerClient.Containers.ExtractArchiveToContainerAsync(
                containerListResponse[0].ID,
                new ContainerPathStatParameters
                {
                    Path = "/*"
                },
                fileStream,
                cts.Token);

            await extractArchiveTask;

            Assert.True(extractArchiveTask.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task GetArchiveFromContainerAsync_StateUnderTest_ExpectedBehavior()
        {

            var cts = new CancellationTokenSource();
            var containerListResponse = await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["ancestor"] = new Dictionary<string, bool>
                        {
                            [$"{IContainerOperationsTests.ImageName}:{_testImageTag}"] = true
                        }
                    }
                },
                cts.Token
            );

            Assert.NotNull(containerListResponse);
            Assert.NotEmpty(containerListResponse);


            var getArchiveFromContainerResponse = await _dockerClient.Containers.GetArchiveFromContainerAsync(
                containerListResponse[0].ID,
                new GetArchiveFromContainerParameters
                {
                    Path = ""
                },
                statOnly: false,
                cts.Token);

            Assert.True(getArchiveFromContainerResponse.Stream.CanRead);
            await getArchiveFromContainerResponse.Stream.DisposeAsync();
        }

        //[Fact]
        public async Task GetContainerLogsAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerLogsParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            var result = await containerOperations.GetContainerLogsAsync(
                id,
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task GetContainerLogsAsync_StateUnderTest_ExpectedBehavior1()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerLogsParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);
            Progress<string> progress = null;

            // Act
            await containerOperations.GetContainerLogsAsync(
                id,
                parameters,
                cancellationToken,
                progress);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task GetContainerLogsAsync_StateUnderTest_ExpectedBehavior2()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            bool tty = false;
            ContainerLogsParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            var result = await containerOperations.GetContainerLogsAsync(
                id,
                tty,
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task GetContainerStatsAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerStatsParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            var result = await containerOperations.GetContainerStatsAsync(
                id,
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task GetContainerStatsAsync_StateUnderTest_ExpectedBehavior1()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerStatsParameters parameters = null;
            IProgress<ContainerStatsResponse> progress = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            await containerOperations.GetContainerStatsAsync(
                id,
                parameters,
                progress,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task InspectChangesAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            var result = await containerOperations.InspectChangesAsync(
                id,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task InspectContainerAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            var result = await containerOperations.InspectContainerAsync(
                id,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task KillContainerAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerKillParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            await containerOperations.KillContainerAsync(
                id,
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        [Fact]
        public async Task ListContainersAsync_ContainerExists_Succeeds()
        {
            var cts = new CancellationTokenSource(delay: TimeSpan.FromMinutes(10));

            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = IContainerOperationsTests.ImageName,
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
                            [$"{IContainerOperationsTests.ImageName}:latest"] = true
                        }
                    }
                },
                cts.Token
            );

            _output.WriteLine($"ImageListResponse: {JsonConvert.SerializeObject(imageListResponse)}");

            await _dockerClient.Images.TagImageAsync(
                $"{IContainerOperationsTests.ImageName}:latest",
                new ImageTagParameters
                {
                    RepositoryName = IContainerOperationsTests.ImageName,
                    Tag = _testImageTag,
                    Force = true
                },
                cts.Token);

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters()
            {
                Image = $"{IContainerOperationsTests.ImageName}:latest",
                Name = Guid.NewGuid().ToString()
            },
            cts.Token);

            _output.WriteLine($"CreateContainerResponse: '{JsonConvert.SerializeObject(createContainerResponse)}'");

            var startContainerTask = _dockerClient.Containers.StartContainerAsync(createContainerResponse.ID, new ContainerStartParameters(), cts.Token);
            var startContainerResult = await startContainerTask;


            var containerListResponse = await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["ancestor"] = new Dictionary<string, bool>
                        {
                            [$"{IContainerOperationsTests.ImageName}:{_testImageTag}"] = true
                        }
                    }
                },
                cts.Token);

            Assert.NotNull(containerListResponse);
            Assert.NotEmpty(containerListResponse);
        }

        //[Fact]
        public async Task ListProcessesAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerListProcessesParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            var result = await containerOperations.ListProcessesAsync(
                id,
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task PauseContainerAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            await containerOperations.PauseContainerAsync(
                id,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task PruneContainersAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            ContainersPruneParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            var result = await containerOperations.PruneContainersAsync(
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task RemoveContainerAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerRemoveParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            await containerOperations.RemoveContainerAsync(
                id,
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task RenameContainerAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerRenameParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            await containerOperations.RenameContainerAsync(
                id,
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task ResizeContainerTtyAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerResizeParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            await containerOperations.ResizeContainerTtyAsync(
                id,
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task RestartContainerAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerRestartParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            await containerOperations.RestartContainerAsync(
                id,
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task StartContainerAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerStartParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            var result = await containerOperations.StartContainerAsync(
                id,
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        [Fact]
        public async Task CreateImageAsync_StartContainerAsync()
        {
            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = ImageName,
                    Tag = "latest",
                },
                authConfig: null,
                progress: new Progress<JSONMessage>((m) => _output.WriteLine(JsonConvert.SerializeObject(m))),
                default);

            var imageListResponse = await _dockerClient.Images.ListImagesAsync(
                new ImagesListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["reference"] = new Dictionary<string, bool>
                        {
                            [ImageName] = true
                        }
                    }
                },
                default);

            Assert.NotNull(imageListResponse);
            Assert.NotEmpty(imageListResponse);
            Assert.NotEmpty(imageListResponse[0].ID);
        }

        //[Fact]
        public async Task UnpauseContainerAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            await containerOperations.UnpauseContainerAsync(
                id,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        //[Fact]
        public async Task UpdateContainerAsync_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            var containerOperations = new ContainerOperations(_dockerClient);
            string id = null;
            ContainerUpdateParameters parameters = null;
            CancellationToken cancellationToken = default(global::System.Threading.CancellationToken);

            // Act
            var result = await containerOperations.UpdateContainerAsync(
                id,
                parameters,
                cancellationToken);

            // Assert
            Assert.True(false);
        }

        [Fact]
        public async Task WaitContainerAsync_TokenIsCancelled_OperationCancelledException()
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var cts = new CancellationTokenSource(delay: TimeSpan.FromMinutes(10));

            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = IContainerOperationsTests.ImageName,
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
                            [ImageName] = true
                        }
                    }
                },
                cts.Token
            );

            Console.WriteLine($"ImageListResponse: {JsonConvert.SerializeObject(imageListResponse)}");

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters()
            {
                Image = ImageName,
                Name = Guid.NewGuid().ToString(),
            },
            cts.Token);

            Console.WriteLine($"CreateContainerResponse: '{JsonConvert.SerializeObject(createContainerResponse)}'");

            var startContainerTask = _dockerClient.Containers.StartContainerAsync(createContainerResponse.ID, new ContainerStartParameters(), cts.Token);
            var startContainerResult = await startContainerTask;

            Console.WriteLine($"StartContainerAsync: {stopWatch.Elapsed} ms, Task.IsCompleted: '{startContainerTask.IsCompleted}', Task.IsCompletedSuccessfully: '{startContainerTask.IsCompletedSuccessfully}', Task.IsCanceled: '{startContainerTask.IsCanceled}', StartContainerResult: '{JsonConvert.SerializeObject(startContainerResult)}'");

            Console.WriteLine("Starting 500 ms timeout to cancel operation.");
            cts.CancelAfter(500);

            // Wait forever unless cancelled
            var waitContainerTask = _dockerClient.Containers.WaitContainerAsync(createContainerResponse.ID, cts.Token);
            ContainerWaitResponse containerWaitResponse = null;

            try
            {
                containerWaitResponse = await waitContainerTask;
            }
            catch (OperationCanceledException ex)
            {
                Console.WriteLine($"WaitContainerTask was cancelled after {stopWatch.ElapsedMilliseconds} ms -> '{ex.Message}'");
                Console.WriteLine($"ContainerWaitResponse -> '{JsonConvert.SerializeObject(containerWaitResponse)}'");
            }

            Console.WriteLine($"WaitContainerAsync: {stopWatch.Elapsed} elapsed");
            Console.WriteLine($"Task.IsCanceled: '{JsonConvert.SerializeObject(waitContainerTask.IsCanceled)}'");
            Console.WriteLine($"Task.IsCompleted: '{JsonConvert.SerializeObject(waitContainerTask.IsCompleted)}'");
            Console.WriteLine($"Task.IsCompletedSuccesfully: '{JsonConvert.SerializeObject(waitContainerTask.IsCompletedSuccessfully)}'");
            Console.WriteLine($"Task.IsFaulted: '{JsonConvert.SerializeObject(waitContainerTask.IsFaulted)}'");
            Console.WriteLine($"ContainerWaitResponse: '{JsonConvert.SerializeObject(containerWaitResponse)}'");

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
            _dockerClient.Dispose();
            _dockerConfiguration.Dispose();
        }
    }
}
