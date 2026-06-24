Imports System.IO
Imports System.Text.Json

Public Module TemplateSnapshotStore

    Private ReadOnly FilePath As String =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "template_snapshot.json")

    Public Sub Save(snapshot As TemplateSnapshot)

        Dim json = JsonSerializer.Serialize(snapshot, New JsonSerializerOptions With {
        .WriteIndented = True
    })

        Dim tmpPath = FilePath & ".tmp"

        File.WriteAllText(tmpPath, json)

        If File.Exists(FilePath) Then
            File.Delete(FilePath)
        End If

        File.Move(tmpPath, FilePath)

    End Sub

    Public Function Load() As TemplateSnapshot

        Try
            If Not File.Exists(FilePath) Then
                Return New TemplateSnapshot()
            End If

            Dim json = File.ReadAllText(FilePath)

            Dim obj = JsonSerializer.Deserialize(Of TemplateSnapshot)(json)

            If obj Is Nothing Then
                Return New TemplateSnapshot()
            End If

            Return obj

        Catch ex As Exception
            Return New TemplateSnapshot()
        End Try

    End Function

End Module