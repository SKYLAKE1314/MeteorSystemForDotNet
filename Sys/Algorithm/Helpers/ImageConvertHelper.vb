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
            Dim type As MatType = MatType.CV_8UC3 ' 預設為 Bgr24
            Dim channels As Integer = 3

            If pf = PixelFormats.Gray8 Then
                type = MatType.CV_8UC1
                channels = 1
            ElseIf pf = PixelFormats.Bgra32 Then
                type = MatType.CV_8UC4
                channels = 4
            ElseIf pf = PixelFormats.Bgr24 Then
                type = MatType.CV_8UC3
                channels = 3
            End If

            Dim stride As Integer = width * channels
            Dim padding = stride Mod 4
            If padding > 0 Then
                stride += (4 - padding)
            End If

            Dim pixels(stride * height - 1) As Byte
            bmp.CopyPixels(pixels, stride, 0)

            Dim mat As New Mat(height, width, type)
            Dim bytesCopied = Math.Min(stride * height, CInt(mat.Step()) * height)
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, mat.Data, bytesCopied)
            Return mat
        Catch ex As Exception
            ' 回退至標準 OpenCvSharp 轉換器
            Return BitmapSourceConverter.ToMat(bmp)
        End Try
    End Function
End Class
