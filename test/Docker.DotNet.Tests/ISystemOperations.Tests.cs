using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Docker.DotNet.Tests
{
    [CollectionDefinition("TestsCollection", DisableParallelization = true)]
    public class ISystemOperationsTests : IDisposable
    {
        private readonly DockerClientConfiguration _dockerClientConfiguration;
        private readonly DockerClient _client;
        private readonly ITestOutputHelper _output;
        private const string _imageName = "nats";
        private readonly string _repositoryName = Guid.NewGuid().ToString();
        private readonly string _tag = Guid.NewGuid().ToString();
        private readonly string _imageId;

        public ISystemOperationsTests(ITestOutputHelper output)
        {
            _output = output;
            _dockerClientConfiguration = new DockerClientConfiguration();
            _client = _dockerClientConfiguration.CreateClient();

            // Prepare image used for tests
            _client.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = "nats",
                    Tag = "latest"
                },
                null,
                new Progress<JSONMessage>((m) => _output.WriteLine($"{m.Progress} - {m.ProgressMessage} - {m.Status}"))
                ).GetAwaiter().GetResult();

            var existingImagesResponse = _client.Images.ListImagesAsync(
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
                default).GetAwaiter().GetResult();

            var existingImageId = existingImagesResponse[0].ID;

            _client.Images.TagImageAsync(
                existingImageId,
                new ImageTagParameters
                {
                    RepositoryName = _repositoryName,
                    Tag = _tag
                },
                default).GetAwaiter().GetResult();

            var imagesListResponse = _client.Images.ListImagesAsync(
                new ImagesListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["reference"] = new Dictionary<string, bool>
                        {
                            [$"{_repositoryName}:{_tag}"] = true
                        }
                    }
                },
                default).GetAwaiter().GetResult();

            _imageId = $"{_repositoryName}:{_tag}";

        }

        [Fact]
        public void Docker_IsRunning()
        {
            var dockerProcess = Process.GetProcesses().FirstOrDefault(_ => _.ProcessName.Equals("docker", StringComparison.InvariantCultureIgnoreCase) || _.ProcessName.Equals("dockerd", StringComparison.InvariantCultureIgnoreCase));
            Assert.NotNull(dockerProcess); // docker is not running
        }

        [Fact]
        public async Task GetSystemInfoAsync_Succeeds()
        {
            var info = await _client.System.GetSystemInfoAsync();
            Assert.NotNull(info.Architecture);
        }

        [Fact]
        public async Task GetVersionAsync_Succeeds()
        {
            var version = await _client.System.GetVersionAsync();
            Assert.NotNull(version.APIVersion);
        }

        [Fact]
        public async Task MonitorEventsAsync_EmptyContainersList_CanBeCancelled()
        {
            var progress = new ProgressMessage()
            {
                _onMessageCalled = (m) => { }
            };

            var cts = new CancellationTokenSource();
            cts.CancelAfter(1000);

            var task = _client.System.MonitorEventsAsync(new ContainerEventsParameters(), progress, cts.Token);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

            Assert.True(task.IsCanceled);

        }

        [Fact]
        public async Task MonitorEventsAsync_NullParameters_Throws()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _client.System.MonitorEventsAsync(null, null));
        }

        [Fact]
        public async Task MonitorEventsAsync_NullProgress_Throws()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _client.System.MonitorEventsAsync(new ContainerEventsParameters(), null));
        }

        [Fact]
        public async Task MonitorEventsAsync_Succeeds()
        {
            var newTag = $"MonitorTests-{Guid.NewGuid().ToString().Substring(1, 10)}";

            var progressJSONMessage = new ProgressJSONMessage
            {
                _onJSONMessageCalled = (m) =>
                {
                    // Status could be 'Pulling from...'
                    _output.WriteLine($"{System.Reflection.MethodBase.GetCurrentMethod().Module}->{System.Reflection.MethodBase.GetCurrentMethod().Name}: _onJSONMessageCalled - {m.ID} - {m.Status} {m.From} - {m.Stream}");
                    Assert.NotNull(m);
                }
            };

            var wasProgressCalled = false;
            var progressMessage = new ProgressMessage
            {
                _onMessageCalled = (m) =>
                {
                    _output.WriteLine($"{System.Reflection.MethodBase.GetCurrentMethod().Module}->{System.Reflection.MethodInfo.GetCurrentMethod().Name}: _onMessageCalled - {m.Action} - {m.Status} {m.From} - {m.Type}");
                    wasProgressCalled = true;
                    Assert.NotNull(m);
                }
            };

            using var cts = new CancellationTokenSource();

            var task = Task.Run(() => _client.System.MonitorEventsAsync(new ContainerEventsParameters(), progressMessage, cts.Token));

            await _client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = _imageName }, null, progressJSONMessage, cts.Token);

            await _client.Images.TagImageAsync(_imageId, new ImageTagParameters { RepositoryName = _repositoryName, Tag = newTag }, cts.Token);

            await _client.Images.DeleteImageAsync(
                name: $"{_repositoryName}:{newTag}",
                new ImageDeleteParameters
                {
                    Force = true
                },
                cts.Token);

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

            Assert.True(wasProgressCalled);
            Assert.True(task.IsCanceled);
        }

        [Fact]
        public void MonitorEventsAsync_IsCancelled_NoStreamCorruption()
        {
            var rand = new Random();
            var sw = new Stopwatch();
            for (int i = 0; i < 100; ++i)
            {
                try
                {
                    // (1) Create monitor task
                    using var cts = new CancellationTokenSource();

                    var monitorTask = _client.System.MonitorEventsAsync(
                        new ContainerEventsParameters(),
                        new Progress<Message>((value) => _output.WriteLine($"DockerSystemEvent: {JsonConvert.SerializeObject(value)}")),
                        cts.Token);

                    // (2) Wait for some time to make sure we get into blocking IO call
                    Thread.Sleep(100);

                    // (3) Invoke another request that will attempt to grab the same buffer
                    var listImagesTask1 = _client.Images.TagImageAsync(
                        _imageId,
                        new ImageTagParameters
                        {
                            RepositoryName = _repositoryName,
                            Tag = _tag,
                            Force = true
                        },
                        default);

                    // (4) Wait for a short bit again and cancel the monitor task - if we get lucky, we the list images call will grab the same buffer while
                    sw.Restart();
                    var iterations = rand.Next(15000000);

                    for (int j = 0; j < iterations; j++)
                    {
                        // noop
                    }
                    _output.WriteLine($"Waited for {sw.Elapsed.TotalMilliseconds} ms");

                    cts.Cancel();

                    listImagesTask1.GetAwaiter().GetResult();
                    _client.Images.TagImageAsync(_imageName, new ImageTagParameters { RepositoryName = _repositoryName, Tag = _tag, Force = true }).GetAwaiter().GetResult();

                    monitorTask.GetAwaiter().GetResult();
                }
                catch (TaskCanceledException)
                {
                    // Exceptions other than this causes test to fail
                }
            }
        }

        [Fact]
        public async Task MonitorEventsFiltered_Succeeds()
        {
            var newTag = $"MonitorTests-{Guid.NewGuid().ToString().Substring(1, 10)}";

            var progressJSONMessage = new ProgressJSONMessage
            {
                _onJSONMessageCalled = (m) => { }
            };

            await _client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = _imageName }, null, progressJSONMessage);

            var progressCalledCounter = 0;

            var eventsParams = new ContainerEventsParameters()
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>()
                {
                    {
                        "event", new Dictionary<string, bool>()
                        {
                            {
                                "tag", true
                            },
                            {
                                "untag", true
                            }
                        }
                    },
                    {
                        "type", new Dictionary<string, bool>()
                        {
                            {
                                "image", true
                            }
                        }
                    }
                }
            };

            var progress = new ProgressMessage()
            {
                _onMessageCalled = (m) =>
                {
                    Console.WriteLine($"{System.Reflection.MethodInfo.GetCurrentMethod().Module}->{System.Reflection.MethodInfo.GetCurrentMethod().Name}: _onMessageCalled received: {m.Action} - {m.Status} {m.From} - {m.Type}");
                    Assert.True(m.Status == "tag" || m.Status == "untag");
                    progressCalledCounter++;
                }
            };

            using var cts = new CancellationTokenSource();
            var task = Task.Run(() => _client.System.MonitorEventsAsync(eventsParams, progress, cts.Token));

            await _client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = _imageName }, null, progressJSONMessage);

            await _client.Images.TagImageAsync(_imageId, new ImageTagParameters { RepositoryName = _repositoryName, Tag = newTag });
            await _client.Images.DeleteImageAsync($"{_repositoryName}:{newTag}", new ImageDeleteParameters());

            var createContainerResponse = await _client.Containers.CreateContainerAsync(new CreateContainerParameters { Image = _imageId });

            await _client.Containers.RemoveContainerAsync(createContainerResponse.ID, new ContainerRemoveParameters(), cts.Token);

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

            Assert.Equal(2, progressCalledCounter);
            Assert.True(task.IsCanceled);
        }

        [Fact]
        public async Task PingAsync_Succeeds()
        {
            await _client.System.PingAsync();
        }

        public void Dispose()
        {
            // Delete image created for tests
            _client.Images.DeleteImageAsync(
                _imageId,
                new ImageDeleteParameters
                {
                    Force = true
                },
                default).GetAwaiter().GetResult();

            _client.Dispose();
            _dockerClientConfiguration.Dispose();
        }

        private class ProgressMessage : IProgress<Message>
        {
            internal Action<Message> _onMessageCalled;

            void IProgress<Message>.Report(Message value)
            {
                _onMessageCalled(value);
            }
        }

        private class ProgressJSONMessage : IProgress<JSONMessage>
        {
            internal Action<JSONMessage> _onJSONMessageCalled;

            void IProgress<JSONMessage>.Report(JSONMessage value)
            {
                _onJSONMessageCalled(value);
            }
        }
    }
}
