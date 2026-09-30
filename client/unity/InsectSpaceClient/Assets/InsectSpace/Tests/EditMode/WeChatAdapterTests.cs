#if INSECTSPACE_WECHAT_SDK
using System;
using System.Net;
using System.Threading;
using InsectSpace.Platform.WeChat;
using NUnit.Framework;

namespace InsectSpace.Tests
{
    public sealed class WeChatAdapterTests
    {
        [Test]
        public void ServerIssuedIpv4DoesNotRequireDesktopDns()
        {
            var addresses = new WeChatAddressResolver().ResolveAsync("127.0.0.1", CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(1, addresses.Length);
            Assert.AreEqual(IPAddress.Loopback, addresses[0]);
        }

        [TestCase("example.invalid")]
        [TestCase("::1")]
        [TestCase(null)]
        public void UnsupportedAddressesFailExplicitly(string host)
        {
            Assert.Throws<NotSupportedException>(() =>
                new WeChatAddressResolver().ResolveAsync(host, CancellationToken.None));
        }

        [Test]
        public void CancelledResolutionCannotProceed()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    new WeChatAddressResolver().ResolveAsync("127.0.0.1", cancellation.Token));
            }
        }
    }
}
#endif
