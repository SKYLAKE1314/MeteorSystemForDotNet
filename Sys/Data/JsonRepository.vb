Imports System.Text.Json
'實作類

Public Class JsonRepository(Of T)
    Implements IJsonRepository(Of T)

    Public Function Load(filePath As String) As T _
        Implements IJsonRepository(Of T).Load

        Try

            If Not IO.File.Exists(filePath) Then
                Return Nothing
            End If

            Dim json = IO.File.ReadAllText(filePath)

            Return JsonSerializer.Deserialize(Of T)(json)

        Catch

            Return Nothing

        End Try

    End Function

    Public Sub Save(filePath As String, data As T) _
        Implements IJsonRepository(Of T).Save

        Dim json = JsonSerializer.Serialize(
            data,
            New JsonSerializerOptions With {
                .WriteIndented = True
            })

        IO.File.WriteAllText(filePath, json)

    End Sub

End Class