Imports Microsoft.Web.WebView2.Core

Class KawaiiJK

    Private Async Sub Window_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        ' 初始化 WebView2
        Await webView.EnsureCoreWebView2Async()
        ' 透明背景
        webView.DefaultBackgroundColor = System.Drawing.Color.Transparent

        ' Assets 目錄
        Dim assetsPath = System.IO.Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory,
    "UI",
    "Live2D",
    "Assets"
)

        ' 映射虛擬主機
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "appassets",
            assetsPath,
            CoreWebView2HostResourceAccessKind.Allow
        )
        '開發者工具  edge'
        'webView.CoreWebView2.OpenDevToolsWindow()

        ' 用虛擬域名打開 HTML
        webView.CoreWebView2.Navigate("https://appassets/live2d.html")
    End Sub
    ' 浮動
    Protected Overrides Sub OnMouseLeftButtonDown(e As MouseButtonEventArgs)
        MyBase.OnMouseLeftButtonDown(e)
        Me.DragMove()
    End Sub

End Class