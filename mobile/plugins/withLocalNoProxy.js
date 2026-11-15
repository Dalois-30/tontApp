/**
 * Expo config plugin: never send loopback traffic (localhost / 127.0.0.1 / ::1) through the
 * system HTTP proxy.
 *
 * Why: some carriers (e.g. Orange APN) push an HTTP proxy with no exclusion list. On mobile
 * data, Android then routes even http://localhost:5243 (adb reverse to the dev API) to the
 * carrier proxy, which answers 504. Other hosts keep using the system proxy unchanged.
 */
const { withMainApplication } = require('@expo/config-plugins');

const IMPORTS = `import com.facebook.react.modules.network.OkHttpClientProvider
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.ProxySelector
import java.net.SocketAddress
import java.net.URI
import java.io.IOException
`;

const SELECTOR = `
/** Direct connection for loopback hosts, system proxy for everything else. */
private object LoopbackBypassProxySelector : ProxySelector() {
  private val loopback = setOf("localhost", "127.0.0.1", "::1", "[::1]")
  override fun select(uri: URI?): List<Proxy> {
    val host = uri?.host?.lowercase()
    if (host != null && host in loopback) return listOf(Proxy.NO_PROXY)
    return ProxySelector.getDefault()?.select(uri) ?: listOf(Proxy.NO_PROXY)
  }
  override fun connectFailed(uri: URI?, sa: SocketAddress?, ioe: IOException?) {
    ProxySelector.getDefault()?.connectFailed(uri, sa, ioe)
  }
}
`;

const INSTALL = `    OkHttpClientProvider.setOkHttpClientFactory {
      OkHttpClientProvider.createClientBuilder().proxySelector(LoopbackBypassProxySelector).build()
    }
`;

module.exports = function withLocalNoProxy(config) {
  return withMainApplication(config, (cfg) => {
    let src = cfg.modResults.contents;
    if (src.includes('LoopbackBypassProxySelector')) return cfg;
    src = src.replace(/(import android\.app\.Application\n)/, `$1${IMPORTS}`);
    src = src.replace(/(override fun onCreate\(\) \{\n\s*super\.onCreate\(\)\n)/, `$1${INSTALL}`);
    src = `${src.trimEnd()}\n${SELECTOR}`;
    cfg.modResults.contents = src;
    return cfg;
  });
};
