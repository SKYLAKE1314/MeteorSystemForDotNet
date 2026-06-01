Imports Microsoft.Win32
Public Class DialogHelper
    Public Shared Function OpenImage() As String

        Dim dialog As New OpenFileDialog()

        dialog.Filter =
            "Image|*.png;*.jpg;*.jpeg;*.bmp"

        If dialog.ShowDialog() = True Then

            Return dialog.FileName

        End If

        Return Nothing

    End Function
End Class
