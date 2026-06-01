Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions
Public Class CvImageHelper
    Public Function ToBitmap(mat As Mat)
        If mat Is Nothing Then
            Return Nothing
        End If

        Return BitmapSourceConverter.ToBitmapSource(mat)
    End Function
End Class
