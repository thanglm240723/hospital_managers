using Yarp.ReverseProxy.Forwarder;

namespace QuanLyBenhVien.IntegrationTests.Gateway;

/// YARP gửi request vào TestServer của API thay vì mạng thật.
public sealed class TestForwarderHttpClientFactory : IForwarderHttpClientFactory
{
    private readonly Func<HttpMessageHandler> _createHandler;

    public TestForwarderHttpClientFactory(Func<HttpMessageHandler> createHandler) => _createHandler = createHandler;

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(_createHandler(), disposeHandler: true);
}
