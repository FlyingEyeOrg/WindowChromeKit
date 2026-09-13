namespace WindowChromeKit.WinForms;

/// <summary>标题栏按钮的语义角色，与 <c>WM_NCHITTEST</c> 的 HT* 值一一对应。</summary>
public enum ChromeCaptionButton
{
    /// <summary>最小化按钮（命中值 <c>HTMINBUTTON</c>）。</summary>
    Minimize,

    /// <summary>最大化/还原按钮（命中值 <c>HTMAXBUTTON</c>，Windows 11 的 Snap Layouts 依赖它）。</summary>
    Maximize,

    /// <summary>关闭按钮（命中值 <c>HTCLOSE</c>）。</summary>
    Close,
}
