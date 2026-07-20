Imports OpenCvSharp
Imports System.Windows.Media.Imaging
Imports System.Runtime.InteropServices

Public Module ImageConvertExt

    Public Function BitmapSourceToMat(bmp As BitmapSource) As Mat
        Return ImageConvertHelper.ToMat(bmp)
    End Function

End Module