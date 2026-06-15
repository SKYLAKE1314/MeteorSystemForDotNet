Imports OpenCvSharp
Imports System.Windows.Media.Imaging
Imports System.Runtime.InteropServices

Public Module ImageConvertExt

    Public Function BitmapSourceToMat(bmp As BitmapSource) As Mat

        Dim encoder As New BmpBitmapEncoder()
        encoder.Frames.Add(BitmapFrame.Create(bmp))

        Using ms As New IO.MemoryStream()
            encoder.Save(ms)
            Dim arr = ms.ToArray()

            Return Cv2.ImDecode(arr, ImreadModes.Color)
        End Using

    End Function

End Module