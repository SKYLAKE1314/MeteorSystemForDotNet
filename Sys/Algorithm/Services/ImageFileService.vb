Imports OpenCvSharp
Public Class ImageFileService
    Public Shared Function Load(path As String) As Mat

        If String.IsNullOrWhiteSpace(path) Then
            Return Nothing
        End If

        Return Cv2.ImRead(path)

    End Function
End Class
