'抽象類別：IJsonRepository
Public Interface IJsonRepository(Of T)

    Function Load(filePath As String) As T

    Sub Save(filePath As String, data As T)

End Interface