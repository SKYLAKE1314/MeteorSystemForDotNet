Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions
Imports System.Windows.Media.Imaging

Public Module ImageToBase64

    Public Function MatToBitmapSource(mat As Mat) As BitmapSource
        If mat Is Nothing Then Return Nothing
        Return BitmapSourceConverter.ToBitmapSource(mat)
    End Function

End Module
