Imports System.Text.Json

Public Module LastTemplateStore

    Private ReadOnly ConfigDir As String =
        IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Config")

    Private ReadOnly FilePath As String =
        IO.Path.Combine(
            ConfigDir,
            "last_template.json")

    Public Sub Save(templatePath As String)

        IO.Directory.CreateDirectory(ConfigDir)

        Dim data As New LastTemplateSnapshot With {
            .LastTemplatePath = templatePath
        }

        Dim json =
            JsonSerializer.Serialize(
                data,
                New JsonSerializerOptions With {
                    .WriteIndented = True
                })

        IO.File.WriteAllText(FilePath, json)

    End Sub

    Public Function Load() As String

        If Not IO.File.Exists(FilePath) Then
            Return Nothing
        End If

        Dim json =
            IO.File.ReadAllText(FilePath)

        Dim data =
            JsonSerializer.Deserialize(
                Of LastTemplateSnapshot)(json)

        Return data?.LastTemplatePath

    End Function

End Module