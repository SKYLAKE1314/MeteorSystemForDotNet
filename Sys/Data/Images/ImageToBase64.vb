Public Class ImageToBase64
    Private Function ImageToBase64(path As String) As String

        If Not IO.File.Exists(path) Then Return ""

        Dim bytes = IO.File.ReadAllBytes(path)
        Return Convert.ToBase64String(bytes)

    End Function
End Class
