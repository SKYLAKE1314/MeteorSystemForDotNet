Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

Public Class ImageConvertHelper
    Public Shared Function ToBitmap(mat As Mat) As BitmapSource
        If mat Is Nothing Then
            Return Nothing
        End If
        Return BitmapSourceConverter.ToBitmapSource(mat)
    End Function

    Public Shared Function ToMat(bmp As BitmapSource) As Mat
        If bmp Is Nothing Then Return Nothing
        Try
            Dim width As Integer = bmp.PixelWidth
            Dim height As Integer = bmp.PixelHeight

            Dim pf = bmp.Format
            Dim bitsPerPixel = pf.BitsPerPixel
            Dim channels As Integer = 3
            Dim type As MatType = MatType.CV_8UC3

            If bitsPerPixel = 8 Then
                type = MatType.CV_8UC1
                channels = 1
            ElseIf bitsPerPixel = 24 Then
                type = MatType.CV_8UC3
                channels = 3
            ElseIf bitsPerPixel = 32 Then
                type = MatType.CV_8UC4
                channels = 4
            Else
                type = MatType.CV_8UC3
                channels = 3
            End If

            ' 確保 Stride 與 WPF 的 BitsPerPixel 對齊，WPF 要求 Stride 必須是 4 位元組對齊
            Dim stride As Integer = ((width * bitsPerPixel + 31) \ 32) * 4

            Dim pixels(stride * height - 1) As Byte
            bmp.CopyPixels(pixels, stride, 0)

            Dim mat As New Mat(height, width, type)
            
            ' 安全複製數據，防止 Stride 不一致導致 Memory Access Violation
            Dim matStride = CInt(mat.Step())
            If stride = matStride Then
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, mat.Data, pixels.Length)
            Else
                ' 逐行複製，處理補齊 Padding 差異
                Dim rowLen = Math.Min(stride, matStride)
                For r As Integer = 0 To height - 1
                    Dim srcOffset = r * stride
                    Dim dstOffset = New IntPtr(mat.Data.ToInt64() + r * matStride)
                    System.Runtime.InteropServices.Marshal.Copy(pixels, srcOffset, dstOffset, rowLen)
                Next
            End If

            Return mat
        Catch ex As Exception
            Logger.Warn($"[ImageConvertHelper] ToMat 異常: {ex.Message}，採用回退機制")
            ' 如果在背景執行緒，為避免 ExecutionEngineException，我們嘗試在 UI 執行緒中安全執行 BitmapSourceConverter.ToMat
            If Application.Current IsNot Nothing AndAlso Not Application.Current.Dispatcher.CheckAccess() Then
                Dim resultMat As Mat = Nothing
                Application.Current.Dispatcher.Invoke(Sub()
                                                          Try
                                                              resultMat = BitmapSourceConverter.ToMat(bmp)
                                                          Catch
                                                          End Try
                                                      End Sub)
                If resultMat IsNot Nothing Then Return resultMat
            End If

            ' 如果已經在 UI 執行緒或 UI 執行緒獲取失敗，才在當前執行緒回退
            Try
                Return BitmapSourceConverter.ToMat(bmp)
            Catch
                Return Nothing
            End Try
        End Try
    End Function
End Class
