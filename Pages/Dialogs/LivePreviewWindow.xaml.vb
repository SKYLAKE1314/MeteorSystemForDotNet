Imports System.Windows.Media.Imaging

Public Class LivePreviewWindow
    ''' <summary>
    ''' 實時更新畫面 (確保在 UI 執行緒執行)
    ''' </summary>
    Public Sub UpdateFrame(frame As BitmapSource)
        If frame Is Nothing Then Return

        ' 使用 Render 優先級，確保畫面滑順且不卡死主邏輯
        Dispatcher.BeginInvoke(Sub()
                                   PreviewImage.Source = frame
                               End Sub, System.Windows.Threading.DispatcherPriority.Render)
    End Sub
End Class