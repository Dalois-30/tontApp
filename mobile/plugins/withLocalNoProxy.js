/**
 * Expo config plugin: never send loopback traffic (localhost / 127.0.0.1 / ::1) through the
 * system HTTP proxy.
 *
 * Why: some carriers (e.g. Orange APN) push an HTTP proxy with no exclusion list. On mobile
 * data, Android then routes even http://localhost:5243 (adb reverse to the dev API) to the
 * carrier proxy, which answers 504. Other hosts keep using the system proxy unchanged.
 */
const { withMainApplication } = require('expo/config-plugins');

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

const IMPORT_ANCHOR = /(import android\.app\.Application\n)/;
const ONCREATE_ANCHOR = /(override fun onCreate\(\) \{\n\s*super\.onCreate\(\)\n)/;

/**
 * Adds the loopback-bypass proxy selector to a MainApplication.kt source. Throws when the template no longer
 * has the expected anchors (a new Expo SDK template) instead of silently shipping an app without the bypass.
 */
function addLoopbackBypass(src) {
  if (src.includes('LoopbackBypassProxySelector')) return src;
  for (const [nom, ancre] of [['import android.app.Application', IMPORT_ANCHOR], ['onCreate() { super.onCreate()', ONCREATE_ANCHOR]]) {
    if (!ancre.test(src)) {
      throw new Error(`withLocalNoProxy: "${nom}" not found in MainApplication.kt; update the plugin for this Expo SDK template.`);
    }
  }
  src = src.replace(IMPORT_ANCHOR, `$1${IMPORTS}`);
  src = src.replace(ONCREATE_ANCHOR, `$1${INSTALL}`);
  return `${src.trimEnd()}\n${SELECTOR}`;
}

function withLocalNoProxy(config) {
  return withMainApplication(config, (cfg) => {
    cfg.modResults.contents = addLoopbackBypass(cfg.modResults.contents);
    return cfg;
  });
}

module.exports = withLocalNoProxy;
module.exports.addLoopbackBypass = addLoopbackBypass;
