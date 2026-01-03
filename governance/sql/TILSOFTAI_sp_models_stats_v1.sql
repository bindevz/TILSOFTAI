/*
  Demo Stored Procedure for: models.stats (contract v1)

  Result sets:
    RS1: TotalCount (int)
    RS2: RangeName breakdown: Key, Label, Count
    RS3: Collection breakdown: Key, Label, Count
    RS4: Season breakdown: Key, Label, Count
*/

CREATE OR ALTER PROCEDURE dbo.TILSOFTAI_sp_models_stats_v1
    @RangeName  VARCHAR(50)  = NULL,
    @ModelCode  VARCHAR(4)   = NULL,
    @ModelName  VARCHAR(200) = NULL,
    @Season     VARCHAR(9)   = NULL,
    @Collection VARCHAR(50)  = NULL,
    @TopN       INT          = 10
AS
BEGIN
    SET NOCOUNT ON;

    -- Normalize empties
    IF (LEN(ISNULL(@RangeName,'')) = 0)  SET @RangeName = NULL;
    IF (LEN(ISNULL(@ModelCode,'')) = 0)  SET @ModelCode = NULL;
    IF (LEN(ISNULL(@ModelName,'')) = 0)  SET @ModelName = NULL;
    IF (LEN(ISNULL(@Season,'')) = 0)     SET @Season = NULL;
    IF (LEN(ISNULL(@Collection,'')) = 0) SET @Collection = NULL;
    IF (@TopN IS NULL OR @TopN <= 0) SET @TopN = 10;

    ;WITH F AS (
        SELECT
            m.ModelID,
            m.ModelUD,
            m.ModelNM,
            m.Season,
            m.Collection,
            m.RangeName
        FROM dbo.Model m
        WHERE
            (@RangeName IS NULL OR m.RangeName = @RangeName)
            AND (@ModelCode IS NULL OR m.ModelUD = @ModelCode)
            AND (@ModelName IS NULL OR m.ModelNM LIKE '%' + @ModelName + '%')
            AND (@Season IS NULL OR m.Season = @Season)
            AND (@Collection IS NULL OR m.Collection = @Collection)
    )
    SELECT COUNT(1) AS TotalCount FROM F;

    SELECT TOP (@TopN)
        ISNULL(RangeName,'') AS [Key],
        ISNULL(RangeName,'') AS [Label],
        COUNT(1) AS [Count]
    FROM F
    GROUP BY RangeName
    ORDER BY COUNT(1) DESC, RangeName ASC;

    SELECT TOP (@TopN)
        ISNULL(Collection,'') AS [Key],
        ISNULL(Collection,'') AS [Label],
        COUNT(1) AS [Count]
    FROM F
    GROUP BY Collection
    ORDER BY COUNT(1) DESC, Collection ASC;

    SELECT TOP (@TopN)
        ISNULL(Season,'') AS [Key],
        ISNULL(Season,'') AS [Label],
        COUNT(1) AS [Count]
    FROM F
    GROUP BY Season
    ORDER BY COUNT(1) DESC, Season ASC;
END
GO
