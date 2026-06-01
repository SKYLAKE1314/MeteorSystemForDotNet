Public Class LocalizationManager

    Public Shared Sub ChangeLanguage(
        languageFile As String)

        Dim dict As New ResourceDictionary()

        dict.Source =
            New Uri(
                $"UI/Languages/document/{languageFile}",
                UriKind.Relative)

        Application.Current.Resources.MergedDictionaries.Clear()

        Application.Current.Resources.MergedDictionaries.Add(dict)

    End Sub

End Class