Public Class LanguageManager

    Private Shared _dict As Dictionary(Of String, String)
    Private Shared _currentLang As String = "zhTW"

    Public Shared Event LanguageChanged As EventHandler

    ' =========================
    ' 當前語言
    ' =========================
    Public Shared ReadOnly Property CurrentLanguage As String
        Get
            Return _currentLang
        End Get
    End Property

    ' =========================
    ' Load Language
    ' =========================
    Public Shared Sub Load(lang As String)

        If lang = _currentLang AndAlso _dict IsNot Nothing Then Return

        _currentLang = lang

        Dim file = IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "UI\Languages\document",
            $"{lang}.json"
        )

        If Not IO.File.Exists(file) Then
            _dict = New Dictionary(Of String, String)()
            RaiseEvent LanguageChanged(Nothing, EventArgs.Empty)
            Return
        End If

        Dim json = IO.File.ReadAllText(file)

        _dict = System.Text.Json.JsonSerializer.Deserialize(
            Of Dictionary(Of String, String))(json
        )

        RaiseEvent LanguageChanged(Nothing, EventArgs.Empty)

    End Sub

    ' =========================
    ' Translate
    ' =========================
    Public Shared Function T(key As String) As String

        If _dict Is Nothing Then Return key

        Dim value As String = Nothing

        If _dict.TryGetValue(key, value) Then
            Return value
        End If

        Return key

    End Function

End Class