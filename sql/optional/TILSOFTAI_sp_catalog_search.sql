IF OBJECT_ID('dbo.TILSOFTAI_sp_catalog_search', 'P') IS NULL
BEGIN
    EXEC('CREATE PROCEDURE dbo.TILSOFTAI_sp_catalog_search AS SELECT 1;');
END
GO

ALTER PROCEDURE dbo.TILSOFTAI_sp_catalog_search
    @query NVARCHAR(256),
    @topK INT = 10
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @q NVARCHAR(256) = LTRIM(RTRIM(ISNULL(@query, N'')));
    DECLARE @like NVARCHAR(260) = N'%' + @q + N'%';

    SELECT TOP (@topK)
        SpName,
        Domain,
        Entity,
        IntentVi,
        IntentEn,
        Tags,
        ParamsJson,
        ExampleJson,
        SchemaHintsJson,
        UpdatedAtUtc,
        CASE
            WHEN SpName = @q THEN 1000
            WHEN SpName LIKE @like THEN 400
            WHEN Tags LIKE @like THEN 200
            WHEN IntentVi LIKE @like THEN 150
            WHEN IntentEn LIKE @like THEN 150
            WHEN Domain LIKE @like THEN 100
            WHEN Entity LIKE @like THEN 100
            ELSE 1
        END AS Score
    FROM dbo.TILSOFTAI_SPCatalog WITH (NOLOCK)
    WHERE IsEnabled = 1 AND IsReadOnly = 1 AND IsAtomicCompatible = 1
      AND (
          SpName LIKE @like
          OR Tags LIKE @like
          OR IntentVi LIKE @like
          OR IntentEn LIKE @like
          OR Domain LIKE @like
          OR Entity LIKE @like
      )
    ORDER BY Score DESC, UpdatedAtUtc DESC;
END
GO