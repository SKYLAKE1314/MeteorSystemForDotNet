Public Class LanguageManager

    Private Shared _dict As Dictionary(Of String, String)

    Public Shared Event LanguageChanged As Action

    Public Shared Sub Load(lang As String)

        Dim file = IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "UI\Languages\document",
            $"{lang}.json")

        Dim json = IO.File.ReadAllText(file)



        _dict = System.Text.Json.JsonSerializer.Deserialize(
Of Dictionary(Of String, String))(json)


        RaiseEvent LanguageChanged()

    End Sub

    Public Shared Function T(key As String) As String

        If _dict Is Nothing Then Return key

        If _dict.ContainsKey(key) Then
            Return _dict(key)
        End If

        Return key

    End Function

End Class