/*
  Demo Stored Procedure for: models.options (contract v1)

  This is a template because option tables differ by implementation.
  Your implementation must return the following result sets (in order):

    RS1 (Model header):
      ModelID (int), ModelCode (varchar), ModelName (nvarchar), Season (varchar), Collection (varchar), RangeName (varchar)

    RS2 (Option groups):
      GroupKey (varchar), GroupName (nvarchar), IsRequired (bit), SortOrder (int)

    RS3 (Option values):
      GroupKey (varchar), ValueKey (varchar), ValueName (nvarchar), SortOrder (int)

    RS4 (Constraints - optional):
      RuleType (varchar), IfGroupKey (varchar), IfValueKey (varchar), ThenGroupKey (varchar), ThenValueKey (varchar), Message (nvarchar)
*/

CREATE OR ALTER PROCEDURE dbo.TILSOFTAI_sp_models_options_v1
    @ModelID INT
AS
BEGIN
    SET NOCOUNT ON;

    /*
      RS1: Model header (replace table/column names if needed)
    */
    SELECT
        m.ModelID,
        m.ModelUD AS ModelCode,
        m.ModelNM AS ModelName,
        m.Season,
        m.Collection,
        m.RangeName
    FROM dbo.Model m
    WHERE m.ModelID = @ModelID;

    /*
      RS2+RS3+RS4: Options and constraints.
      Replace the placeholder queries below with your real tables.

      Typical structures:
        - dbo.ModelOptionGroup   (ModelID, GroupKey, GroupName, IsRequired, SortOrder)
        - dbo.ModelOptionValue   (ModelID, GroupKey, ValueKey, ValueName, SortOrder)
        - dbo.ModelOptionRule    (ModelID, RuleType, IfGroupKey, IfValueKey, ThenGroupKey, ThenValueKey, Message)
    */

    -- RS2
    SELECT
        CAST('FRAME_MATERIAL' AS VARCHAR(50)) AS GroupKey,
        CAST(N'Khung' AS NVARCHAR(200)) AS GroupName,
        CAST(1 AS BIT) AS IsRequired,
        CAST(10 AS INT) AS SortOrder
    WHERE 1 = 0; -- replace

    -- RS3
    SELECT
        CAST('FRAME_MATERIAL' AS VARCHAR(50)) AS GroupKey,
        CAST('AL' AS VARCHAR(50)) AS ValueKey,
        CAST(N'Nhôm' AS NVARCHAR(200)) AS ValueName,
        CAST(10 AS INT) AS SortOrder
    WHERE 1 = 0; -- replace

    -- RS4
    SELECT
        CAST('DISALLOW' AS VARCHAR(30)) AS RuleType,
        CAST('FRAME_MATERIAL' AS VARCHAR(50)) AS IfGroupKey,
        CAST('AL' AS VARCHAR(50)) AS IfValueKey,
        CAST('LEG_MATERIAL' AS VARCHAR(50)) AS ThenGroupKey,
        CAST('WOOD' AS VARCHAR(50)) AS ThenValueKey,
        CAST(N'Khung nhôm không đi với chân gỗ trong model này.' AS NVARCHAR(500)) AS Message
    WHERE 1 = 0; -- replace
END
GO
