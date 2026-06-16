Imports OpenCvSharp
Imports System.Windows.Media.Imaging

Public Class Laplacian

    Public Shared Function GetScore(
        bmp As BitmapSource) As Double

        Dim mat = BitmapSourceToMat(bmp)

        Dim gray As New Mat()

        If mat.Channels() = 3 Then

            Cv2.CvtColor(
                mat,
                gray,
                ColorConversionCodes.BGR2GRAY)

        Else

            gray = mat.Clone()

        End If

        Dim lap As New Mat()

        Cv2.Laplacian(
            gray,
            lap,
            MatType.CV_64F)

        Dim mean As Scalar
        Dim stddev As Scalar

        Cv2.MeanStdDev(
            lap,
            mean,
            stddev)

        Return stddev.Val0 * stddev.Val0

    End Function

End Class