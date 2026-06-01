Imports System.IO
Imports System.Text.Json

Public Module TemplateSnapshotStore

    Private ReadOnly FilePath As String =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "template_snapshot.json")

    Public Sub Save(snapshot As TemplateSnapshot)

        Dim json = JsonSerializer.Serialize(snapshot, New JsonSerializerOptions With {
            .WriteIndented = True
        })

        File.WriteAllText(FilePath, json)

    End Sub

    Public Function Load() As TemplateSnapshot

        If Not File.Exists(FilePath) Then Return Nothing

        Dim json = File.ReadAllText(FilePath)
        Return JsonSerializer.Deserialize(Of TemplateSnapshot)(json)

    End Function

End Module