Imports System.Windows.Media.Imaging

Public Class LivePreviewWindow
    ''' <summary>
    ''' 實時更新畫面 (確保零延遲滑順渲染)
    ''' </summary>
    Public Sub UpdateFrame(frame As BitmapSource)
        If frame Is Nothing Then Return

        If Dispatcher.CheckAccess() Then
            PreviewImage.Source = frame
        Else
            Dispatcher.BeginInvoke(Sub()
                                       PreviewImage.Source = frame
                                   End Sub, System.Windows.Threading.DispatcherPriority.Render)
        End If
    End Sub

    Public Sub UpdateOcrResult(text As String)
        If Dispatcher.CheckAccess() Then
            If String.IsNullOrWhiteSpace(text) Then
                OcrResultBorder.Visibility = System.Windows.Visibility.Collapsed
            Else
                OcrResultText.Text = text
                OcrResultBorder.Visibility = System.Windows.Visibility.Visible
            End If
        Else
            Dispatcher.BeginInvoke(Sub()
                                       If String.IsNullOrWhiteSpace(text) Then
                                           OcrResultBorder.Visibility = System.Windows.Visibility.Collapsed
                                       Else
                                           OcrResultText.Text = text
                                           OcrResultBorder.Visibility = System.Windows.Visibility.Visible
                                       End If
                                   End Sub)
        End If
    End Sub
End Class