using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Docker.DotNet.Models
{
    internal static class StreamUtil
    {
        private static readonly Newtonsoft.Json.JsonSerializer _serializer = new Newtonsoft.Json.JsonSerializer();

        internal static async Task MonitorStreamAsync<T>(Task<Stream> streamTask, DockerClient client, CancellationToken cancel, IProgress<T> progress)
        {
            await MonitorStreamForMessagesAsync<T>(streamTask, client, cancel, progress);
        }

        internal static async Task MonitorStreamForMessagesAsync<T>(Task<Stream> streamTask, DockerClient client, CancellationToken cancel, IProgress<T> progress)
        {
            var tcs = new TaskCompletionSource<bool>();

            using (var stream = await streamTask)
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            using (var jsonReader = new JsonTextReader(reader) { SupportMultipleContent = true })
            using (cancel.Register(() => tcs.TrySetCanceled(cancel)))
            {
                while (await await Task.WhenAny(jsonReader.ReadAsync(default), tcs.Task))
                {
                    var ev = _serializer.Deserialize<T>(jsonReader);
                    progress.Report(ev);
                }
            }
        }

        internal static async Task MonitorResponseForMessagesAsync<T>(Task<HttpResponseMessage> responseTask, DockerClient client, CancellationToken cancel, IProgress<T> progress)
        {
            using (var response = await responseTask)
            {
                await MonitorStreamForMessagesAsync<T>(response.Content.ReadAsStreamAsync(), client, cancel, progress);
            }
        }
    }
}
