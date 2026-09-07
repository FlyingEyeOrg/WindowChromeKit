using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>在标准 WebView2 自身的 HWND 内遮盖导航和复用切换过程。</summary>
internal static class WebViewPresentationMask
{
    public const string InitializationScript = """
        (() => {
          const hostId = '__softwarehub_presentation_mask__';
          let visible = true;
          let generation = 0;

          const ensureMask = () => {
            const root = document.documentElement;
            if (!root) return null;
            let host = document.getElementById(hostId);
            if (!host) {
              host = document.createElement('div');
              host.id = hostId;
              host.setAttribute('aria-hidden', 'true');
              Object.assign(host.style, {
                position: 'fixed', inset: '0', zIndex: '2147483647',
                display: 'grid', placeItems: 'center', background: '#ffffff',
                pointerEvents: 'auto'
              });
              const shadow = host.attachShadow({ mode: 'closed' });
              const content = document.createElement('div');
              Object.assign(content.style, {
                color:'#666', font:'14px "Segoe UI",sans-serif', textAlign:'center'
              });
              const spinner = document.createElement('i');
              Object.assign(spinner.style, {
                display:'block', width:'26px', height:'26px', margin:'0 auto 12px',
                border:'3px solid #ddd', borderTopColor:'#2878d7', borderRadius:'50%'
              });
              spinner.animate(
                [{ transform:'rotate(0deg)' }, { transform:'rotate(360deg)' }],
                { duration:800, iterations:Infinity });
              const label = document.createElement('span');
              label.textContent = '正在加载…';
              content.append(spinner, label);
              shadow.appendChild(content);
              root.appendChild(host);
            }
            host.style.display = visible ? 'grid' : 'none';
            return host;
          };

          ensureMask();
          if (!document.documentElement) {
            new MutationObserver((_, observer) => {
              if (ensureMask()) observer.disconnect();
            }).observe(document, { childList: true, subtree: true });
          }

          const setVisibility = (nextVisible, nextGeneration) => {
            const requestedGeneration = Number(nextGeneration) || 0;
            if (requestedGeneration < generation) return ensureMask() !== null;
            generation = requestedGeneration;
            visible = nextVisible === true;
            const mask = ensureMask();
            return mask !== null;
          };
          Object.defineProperty(globalThis, '__softwareHubSetPresentationMask', {
            value: setVisibility
          });
          chrome.webview.addEventListener('message', event => {
            const message = event.data;
            if (message?.type !== 'presentationMask') return;
            setVisibility(message.visible, message.generation);
          });
        })();
        """;

    public static string CreateCommand(bool visible, long generation) =>
        JsonSerializer.Serialize(new { type = "presentationMask", visible, generation });

    public static void PostVisibility(CoreWebView2 core, bool visible, long generation)
    {
        ArgumentNullException.ThrowIfNull(core);
        core.PostWebMessageAsJson(CreateCommand(visible, generation));
    }

    public static async Task<bool> SetVisibilityAsync(
        CoreWebView2 core,
        bool visible,
        long generation,
        CancellationToken token)
    {
        var script =
            $"globalThis.__softwareHubSetPresentationMask?.({visible.ToString().ToLowerInvariant()}, {generation}) === true";
        var result = await core.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(15), token);
        return string.Equals(result, "true", StringComparison.Ordinal);
    }
}
