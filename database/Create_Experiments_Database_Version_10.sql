USE [master]
GO
/****** Object:  Database [Experiments]    Script Date: 10/23/2026 12:49:17 PM ******/
CREATE DATABASE [Experiments]
 CONTAINMENT = NONE
 ON  PRIMARY 
( NAME = N'Experiments', FILENAME = N'C:\Program Files\Microsoft SQL Server\MSSQL17.MSSQLSERVER\MSSQL\DATA\Experiments.mdf' , SIZE = 73728KB , MAXSIZE = UNLIMITED, FILEGROWTH = 65536KB )
 LOG ON 
( NAME = N'Experiments_log', FILENAME = N'C:\Program Files\Microsoft SQL Server\MSSQL17.MSSQLSERVER\MSSQL\DATA\Experiments_log.ldf' , SIZE = 73728KB , MAXSIZE = 2048GB , FILEGROWTH = 65536KB )
 WITH CATALOG_COLLATION = DATABASE_DEFAULT, LEDGER = OFF
GO
ALTER DATABASE [Experiments] SET COMPATIBILITY_LEVEL = 170
GO
IF (1 = FULLTEXTSERVICEPROPERTY('IsFullTextInstalled'))
begin
EXEC [Experiments].[dbo].[sp_fulltext_database] @action = 'enable'
end
GO
ALTER DATABASE [Experiments] SET ANSI_NULL_DEFAULT OFF 
GO
ALTER DATABASE [Experiments] SET ANSI_NULLS OFF 
GO
ALTER DATABASE [Experiments] SET ANSI_PADDING OFF 
GO
ALTER DATABASE [Experiments] SET ANSI_WARNINGS OFF 
GO
ALTER DATABASE [Experiments] SET ARITHABORT OFF 
GO
ALTER DATABASE [Experiments] SET AUTO_CLOSE OFF 
GO
ALTER DATABASE [Experiments] SET AUTO_SHRINK OFF 
GO
ALTER DATABASE [Experiments] SET AUTO_UPDATE_STATISTICS ON 
GO
ALTER DATABASE [Experiments] SET CURSOR_CLOSE_ON_COMMIT OFF 
GO
ALTER DATABASE [Experiments] SET CURSOR_DEFAULT  GLOBAL 
GO
ALTER DATABASE [Experiments] SET CONCAT_NULL_YIELDS_NULL OFF 
GO
ALTER DATABASE [Experiments] SET NUMERIC_ROUNDABORT OFF 
GO
ALTER DATABASE [Experiments] SET QUOTED_IDENTIFIER OFF 
GO
ALTER DATABASE [Experiments] SET RECURSIVE_TRIGGERS OFF 
GO
ALTER DATABASE [Experiments] SET  DISABLE_BROKER 
GO
ALTER DATABASE [Experiments] SET AUTO_UPDATE_STATISTICS_ASYNC OFF 
GO
ALTER DATABASE [Experiments] SET DATE_CORRELATION_OPTIMIZATION OFF 
GO
ALTER DATABASE [Experiments] SET TRUSTWORTHY OFF 
GO
ALTER DATABASE [Experiments] SET ALLOW_SNAPSHOT_ISOLATION OFF 
GO
ALTER DATABASE [Experiments] SET PARAMETERIZATION SIMPLE 
GO
ALTER DATABASE [Experiments] SET READ_COMMITTED_SNAPSHOT OFF 
GO
ALTER DATABASE [Experiments] SET HONOR_BROKER_PRIORITY OFF 
GO
ALTER DATABASE [Experiments] SET RECOVERY FULL 
GO
ALTER DATABASE [Experiments] SET  MULTI_USER 
GO
ALTER DATABASE [Experiments] SET PAGE_VERIFY CHECKSUM  
GO
ALTER DATABASE [Experiments] SET DB_CHAINING OFF 
GO
ALTER DATABASE [Experiments] SET FILESTREAM( NON_TRANSACTED_ACCESS = OFF ) 
GO
ALTER DATABASE [Experiments] SET TARGET_RECOVERY_TIME = 60 SECONDS 
GO
ALTER DATABASE [Experiments] SET DELAYED_DURABILITY = DISABLED 
GO
ALTER DATABASE [Experiments] SET OPTIMIZED_LOCKING = OFF 
GO
ALTER DATABASE [Experiments] SET ACCELERATED_DATABASE_RECOVERY = OFF  
GO
EXEC sys.sp_db_vardecimal_storage_format N'Experiments', N'ON'
GO
ALTER DATABASE [Experiments] SET QUERY_STORE = ON
GO
ALTER DATABASE [Experiments] SET QUERY_STORE (OPERATION_MODE = READ_WRITE, CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30), DATA_FLUSH_INTERVAL_SECONDS = 900, INTERVAL_LENGTH_MINUTES = 60, MAX_STORAGE_SIZE_MB = 1000, QUERY_CAPTURE_MODE = AUTO, SIZE_BASED_CLEANUP_MODE = AUTO, MAX_PLANS_PER_QUERY = 200, WAIT_STATS_CAPTURE_MODE = ON)
GO
USE [Experiments]
GO
/****** Object:  UserDefinedTableType [dbo].[VersionIdList]    Script Date: 10/23/2026 12:49:17 PM ******/
CREATE TYPE [dbo].[VersionIdList] AS TABLE(
	[ExperimentVersionID] [bigint] NOT NULL,
	PRIMARY KEY CLUSTERED 
(
	[ExperimentVersionID] ASC
)WITH (IGNORE_DUP_KEY = OFF)
)
GO
/****** Object:  UserDefinedFunction [dbo].[fn_VersionHasExecutions]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   FUNCTION [dbo].[fn_VersionHasExecutions] (@ExperimentVersionID bigint)
RETURNS bit
AS
BEGIN
    RETURN CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Execution AS e
        JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
        WHERE i.ExperimentVersionID = @ExperimentVersionID
    ) THEN 1 ELSE 0 END;
END

GO
/****** Object:  Table [dbo].[Iteration]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Iteration](
	[IterationID] [bigint] IDENTITY(1,1) NOT NULL,
	[ExperimentVersionID] [bigint] NOT NULL,
	[Sequence] [int] NOT NULL,
	[IterationLabel] [nvarchar](100) NOT NULL,
	[Prompt] [nvarchar](max) NOT NULL,
	[Scenario] [nvarchar](100) NULL,
	[Notes] [nvarchar](max) NULL,
 CONSTRAINT [PK_Iteration] PRIMARY KEY CLUSTERED 
(
	[IterationID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_ExperimentVersionID_Sequence] UNIQUE NONCLUSTERED 
(
	[ExperimentVersionID] ASC,
	[Sequence] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  View [dbo].[vOwnerIteration]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Direct owners (the table carries ExperimentVersionID).
CREATE   VIEW [dbo].[vOwnerIteration] AS
SELECT i.IterationID, i.ExperimentVersionID
FROM dbo.Iteration AS i;

GO
/****** Object:  Table [dbo].[Parameter]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Parameter](
	[ParameterID] [bigint] IDENTITY(1,1) NOT NULL,
	[ExperimentVersionID] [bigint] NOT NULL,
	[Name] [nvarchar](200) NOT NULL,
	[Value] [nvarchar](400) NULL,
	[Unit] [nvarchar](50) NULL,
	[Notes] [nvarchar](max) NULL,
 CONSTRAINT [PK_Parameter] PRIMARY KEY CLUSTERED 
(
	[ParameterID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UC_Parameter_ExperimentVersionID_Name] UNIQUE NONCLUSTERED 
(
	[ExperimentVersionID] ASC,
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  View [dbo].[vOwnerParameter]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   VIEW [dbo].[vOwnerParameter] AS
SELECT p.ParameterID, p.ExperimentVersionID
FROM dbo.Parameter AS p;

GO
/****** Object:  Table [dbo].[ScoreRule]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ScoreRule](
	[ScoreRuleID] [int] IDENTITY(1,1) NOT NULL,
	[ExperimentVersionID] [bigint] NOT NULL,
	[PhaseID] [int] NOT NULL,
	[ScoreCategoryID] [int] NOT NULL,
	[WeightPercent] [decimal](5, 2) NOT NULL,
	[MinScore] [int] NOT NULL,
	[MaxScore] [int] NOT NULL,
	[Active] [bit] NOT NULL,
	[RuleNotes] [nvarchar](300) NULL,
 CONSTRAINT [PK_ScoreRule] PRIMARY KEY CLUSTERED 
(
	[ScoreRuleID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UC_ScoreRule_ExperimentVersionID_PhaseID_ScoreCategoryID] UNIQUE NONCLUSTERED 
(
	[ExperimentVersionID] ASC,
	[PhaseID] ASC,
	[ScoreCategoryID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  View [dbo].[vOwnerScoreRule]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   VIEW [dbo].[vOwnerScoreRule] AS
SELECT r.ScoreRuleID, r.ExperimentVersionID
FROM dbo.ScoreRule AS r;

GO
/****** Object:  Table [dbo].[ScoreObservationSnapshot]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ScoreObservationSnapshot](
	[ExperimentVersionID] [bigint] NOT NULL,
	[ExecutionID] [bigint] NOT NULL,
	[PhaseID] [int] NOT NULL,
	[ScoreCategoryID] [int] NOT NULL,
	[ScoreRuleID] [int] NOT NULL,
	[ScoreObservationID] [bigint] NOT NULL,
	[ObservedScore] [decimal](5, 2) NOT NULL,
	[MinScore] [int] NOT NULL,
	[MaxScore] [int] NOT NULL,
	[WeightPercent] [decimal](5, 2) NOT NULL,
	[HigherIsBetter] [bit] NOT NULL,
	[SnapshotUTC] [datetime2](3) NOT NULL,
 CONSTRAINT [PK_ScoreObservationSnapshot] PRIMARY KEY CLUSTERED 
(
	[ExperimentVersionID] ASC,
	[ExecutionID] ASC,
	[PhaseID] ASC,
	[ScoreCategoryID] ASC,
	[ScoreRuleID] ASC,
	[ScoreObservationID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  View [dbo].[vOwnerScoreObservationSnapshot]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   VIEW [dbo].[vOwnerScoreObservationSnapshot] AS
SELECT s.ExperimentVersionID, s.ExecutionID, s.PhaseID, s.ScoreCategoryID, s.ScoreRuleID, s.ScoreObservationID
FROM dbo.ScoreObservationSnapshot AS s;

GO
/****** Object:  Table [dbo].[Execution]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Execution](
	[ExecutionID] [bigint] IDENTITY(1,1) NOT NULL,
	[IterationID] [bigint] NOT NULL,
	[AgentID] [int] NOT NULL,
	[PhaseID] [int] NOT NULL,
	[StatusID] [int] NOT NULL,
	[StartUTC] [datetime2](3) NOT NULL,
	[EndUTC] [datetime2](3) NULL,
	[ModelUsed] [nvarchar](200) NULL,
	[InterfaceUsed] [nvarchar](100) NULL,
	[ArtifactURI] [nvarchar](2048) NULL,
	[InterventionCount] [int] NOT NULL,
	[OutcomeNotes] [nvarchar](max) NULL,
	[AttemptNumber] [int] NOT NULL,
 CONSTRAINT [PK_Execution] PRIMARY KEY CLUSTERED 
(
	[ExecutionID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  View [dbo].[vOwnerExecution]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Indirect owners.
CREATE   VIEW [dbo].[vOwnerExecution] AS
SELECT e.ExecutionID, i.ExperimentVersionID
FROM dbo.Execution AS e
JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID;

GO
/****** Object:  Table [dbo].[Artifact]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Artifact](
	[ArtifactID] [bigint] IDENTITY(1,1) NOT NULL,
	[ExecutionID] [bigint] NOT NULL,
	[ArtifactTypeID] [int] NOT NULL,
	[OriginalName] [nvarchar](200) NOT NULL,
	[OriginalURI] [nvarchar](2048) NOT NULL,
	[StoredURI] [nvarchar](2048) NOT NULL,
	[MimeType] [nvarchar](100) NOT NULL,
	[ByteLength] [bigint] NOT NULL,
	[SHA256] [varchar](64) NOT NULL,
	[ReceivedUTC] [datetime2](3) NOT NULL,
	[StoredUTC] [datetime2](3) NULL,
 CONSTRAINT [PK_Artifact] PRIMARY KEY CLUSTERED 
(
	[ArtifactID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  View [dbo].[vOwnerArtifact]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   VIEW [dbo].[vOwnerArtifact] AS
SELECT a.ArtifactID, o.ExperimentVersionID
FROM dbo.Artifact AS a
JOIN dbo.vOwnerExecution AS o ON o.ExecutionID = a.ExecutionID;

GO
/****** Object:  Table [dbo].[ScoreObservation]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ScoreObservation](
	[ScoreObservationID] [bigint] IDENTITY(1,1) NOT NULL,
	[ExecutionID] [bigint] NOT NULL,
	[ScoreRuleID] [int] NOT NULL,
	[RaterID] [int] NOT NULL,
	[ObservedScore] [decimal](5, 2) NOT NULL,
	[ObservedUTC] [datetime2](3) NULL,
	[EvidenceURI] [nvarchar](2048) NULL,
	[ObservationNotes] [nvarchar](max) NULL,
 CONSTRAINT [PK_ScoreObservation] PRIMARY KEY CLUSTERED 
(
	[ScoreObservationID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UC_ScoreObservation_ExecutionID_ScoreRuleID_RaterID] UNIQUE NONCLUSTERED 
(
	[ExecutionID] ASC,
	[ScoreRuleID] ASC,
	[RaterID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  View [dbo].[vOwnerScoreObservation]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   VIEW [dbo].[vOwnerScoreObservation] AS
SELECT so.ScoreObservationID, o.ExperimentVersionID
FROM dbo.ScoreObservation AS so
JOIN dbo.vOwnerExecution AS o ON o.ExecutionID = so.ExecutionID;

GO
/****** Object:  Table [dbo].[ScoreObservationEvidence]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ScoreObservationEvidence](
	[ScoreObservationID] [bigint] NOT NULL,
	[ArtifactID] [bigint] NOT NULL,
	[EvidenceNotes] [nvarchar](500) NULL,
 CONSTRAINT [PK_ScoreObservationEvidence] PRIMARY KEY CLUSTERED 
(
	[ScoreObservationID] ASC,
	[ArtifactID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  View [dbo].[vOwnerScoreObservationEvidence]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- An evidence link joins an observation to an artifact, so it has two owning versions
-- (the API requires them to be the same; the database does not, so both are guarded).
CREATE   VIEW [dbo].[vOwnerScoreObservationEvidence] AS
SELECT soe.ScoreObservationID, soe.ArtifactID,
       obs.ExperimentVersionID AS ObservationVersionID,
       art.ExperimentVersionID AS ArtifactVersionID
FROM dbo.ScoreObservationEvidence AS soe
JOIN dbo.vOwnerScoreObservation AS obs ON obs.ScoreObservationID = soe.ScoreObservationID
JOIN dbo.vOwnerArtifact AS art ON art.ArtifactID = soe.ArtifactID;

GO
/****** Object:  Table [dbo].[Agent]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Agent](
	[AgentID] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[Provider] [nvarchar](100) NOT NULL,
	[DefaultModel] [nvarchar](200) NOT NULL,
	[DefaultInterface] [nvarchar](100) NOT NULL,
	[Active] [bit] NOT NULL,
 CONSTRAINT [PK_Agent] PRIMARY KEY CLUSTERED 
(
	[AgentID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UC_Agent_Name] UNIQUE NONCLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ArtifactType]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ArtifactType](
	[ArtifactTypeID] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](50) NOT NULL,
	[Description] [nvarchar](300) NULL,
 CONSTRAINT [PK_ArtifactType] PRIMARY KEY CLUSTERED 
(
	[ArtifactTypeID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_ArtifactType_Name] UNIQUE NONCLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ETLSourceAudit]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ETLSourceAudit](
	[WorkbookSHA256] [varchar](64) NOT NULL,
	[SheetName] [nvarchar](100) NOT NULL,
	[ExcelRow] [int] NOT NULL,
	[SourceKey] [nvarchar](200) NOT NULL,
	[SourcePayload] [nvarchar](max) NOT NULL,
	[TargetTable] [sysname] NULL,
	[TargetID] [bigint] NULL,
 CONSTRAINT [PK_ETLSourceAudit] PRIMARY KEY CLUSTERED 
(
	[WorkbookSHA256] ASC,
	[SheetName] ASC,
	[SourceKey] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ExperimentVersion]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ExperimentVersion](
	[ExperimentVersionID] [bigint] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](200) NOT NULL,
	[VersionLabel] [nvarchar](50) NOT NULL,
	[StatusID] [int] NOT NULL,
	[EffectiveDateUTC] [datetime2](3) NOT NULL,
	[Description] [nvarchar](max) NOT NULL,
	[LockedUTC] [datetime2](3) NULL,
 CONSTRAINT [PK_ExperimentVersion] PRIMARY KEY CLUSTERED 
(
	[ExperimentVersionID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_Name_VersionLabel] UNIQUE NONCLUSTERED 
(
	[Name] ASC,
	[VersionLabel] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Phase]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Phase](
	[PhaseID] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[Sequence] [int] NOT NULL,
	[Description] [nvarchar](300) NULL,
 CONSTRAINT [PK_Phase] PRIMARY KEY CLUSTERED 
(
	[PhaseID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UC_Phase_Name] UNIQUE NONCLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UC_Phase_Sequence] UNIQUE NONCLUSTERED 
(
	[Sequence] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Rater]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Rater](
	[RaterID] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[RaterType] [nvarchar](50) NULL,
	[AgentID] [int] NULL,
 CONSTRAINT [PK_Rater] PRIMARY KEY CLUSTERED 
(
	[RaterID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UC_Rater_Name] UNIQUE NONCLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ScoreCategory]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ScoreCategory](
	[ScoreCategoryID] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](50) NOT NULL,
	[Description] [nvarchar](200) NULL,
	[HigherIsBetter] [bit] NOT NULL,
 CONSTRAINT [PK_ScoreCategory] PRIMARY KEY CLUSTERED 
(
	[ScoreCategoryID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UC_ScoreCategory_Name] UNIQUE NONCLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Status]    Script Date: 10/23/2026 12:49:17 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Status](
	[StatusID] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](50) NOT NULL,
	[Description] [nvarchar](50) NULL,
 CONSTRAINT [PK_Status] PRIMARY KEY CLUSTERED 
(
	[StatusID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UC_Status_Name] UNIQUE NONCLUSTERED 
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[Agent] ADD  CONSTRAINT [DF_Agent_Active]  DEFAULT ((1)) FOR [Active]
GO
ALTER TABLE [dbo].[Execution] ADD  CONSTRAINT [DF_Execution_AttemptNumber]  DEFAULT ((1)) FOR [AttemptNumber]
GO
ALTER TABLE [dbo].[Artifact]  WITH CHECK ADD  CONSTRAINT [FK_Artifact_ArtifactType] FOREIGN KEY([ArtifactTypeID])
REFERENCES [dbo].[ArtifactType] ([ArtifactTypeID])
GO
ALTER TABLE [dbo].[Artifact] CHECK CONSTRAINT [FK_Artifact_ArtifactType]
GO
ALTER TABLE [dbo].[Artifact]  WITH CHECK ADD  CONSTRAINT [FK_Artifact_Execution] FOREIGN KEY([ExecutionID])
REFERENCES [dbo].[Execution] ([ExecutionID])
GO
ALTER TABLE [dbo].[Artifact] CHECK CONSTRAINT [FK_Artifact_Execution]
GO
ALTER TABLE [dbo].[Execution]  WITH CHECK ADD  CONSTRAINT [FK_Execution_Agent] FOREIGN KEY([AgentID])
REFERENCES [dbo].[Agent] ([AgentID])
GO
ALTER TABLE [dbo].[Execution] CHECK CONSTRAINT [FK_Execution_Agent]
GO
ALTER TABLE [dbo].[Execution]  WITH CHECK ADD  CONSTRAINT [FK_Execution_Iteration] FOREIGN KEY([IterationID])
REFERENCES [dbo].[Iteration] ([IterationID])
GO
ALTER TABLE [dbo].[Execution] CHECK CONSTRAINT [FK_Execution_Iteration]
GO
ALTER TABLE [dbo].[Execution]  WITH CHECK ADD  CONSTRAINT [FK_Execution_Phase] FOREIGN KEY([PhaseID])
REFERENCES [dbo].[Phase] ([PhaseID])
GO
ALTER TABLE [dbo].[Execution] CHECK CONSTRAINT [FK_Execution_Phase]
GO
ALTER TABLE [dbo].[Execution]  WITH CHECK ADD  CONSTRAINT [FK_Execution_Status] FOREIGN KEY([StatusID])
REFERENCES [dbo].[Status] ([StatusID])
GO
ALTER TABLE [dbo].[Execution] CHECK CONSTRAINT [FK_Execution_Status]
GO
ALTER TABLE [dbo].[ExperimentVersion]  WITH CHECK ADD  CONSTRAINT [FK_ExperimentVersion_Status] FOREIGN KEY([StatusID])
REFERENCES [dbo].[Status] ([StatusID])
GO
ALTER TABLE [dbo].[ExperimentVersion] CHECK CONSTRAINT [FK_ExperimentVersion_Status]
GO
ALTER TABLE [dbo].[Iteration]  WITH CHECK ADD  CONSTRAINT [FK_Iteration_ExperimentVersion] FOREIGN KEY([ExperimentVersionID])
REFERENCES [dbo].[ExperimentVersion] ([ExperimentVersionID])
GO
ALTER TABLE [dbo].[Iteration] CHECK CONSTRAINT [FK_Iteration_ExperimentVersion]
GO
ALTER TABLE [dbo].[Parameter]  WITH CHECK ADD  CONSTRAINT [FK_Parameter_ExperimentVersion] FOREIGN KEY([ExperimentVersionID])
REFERENCES [dbo].[ExperimentVersion] ([ExperimentVersionID])
GO
ALTER TABLE [dbo].[Parameter] CHECK CONSTRAINT [FK_Parameter_ExperimentVersion]
GO
ALTER TABLE [dbo].[Rater]  WITH CHECK ADD  CONSTRAINT [FK_Rater_Agent] FOREIGN KEY([AgentID])
REFERENCES [dbo].[Agent] ([AgentID])
GO
ALTER TABLE [dbo].[Rater] CHECK CONSTRAINT [FK_Rater_Agent]
GO
ALTER TABLE [dbo].[ScoreObservation]  WITH CHECK ADD  CONSTRAINT [FK_ScoreObservation_Execution] FOREIGN KEY([ExecutionID])
REFERENCES [dbo].[Execution] ([ExecutionID])
GO
ALTER TABLE [dbo].[ScoreObservation] CHECK CONSTRAINT [FK_ScoreObservation_Execution]
GO
ALTER TABLE [dbo].[ScoreObservation]  WITH CHECK ADD  CONSTRAINT [FK_ScoreObservation_Rater] FOREIGN KEY([RaterID])
REFERENCES [dbo].[Rater] ([RaterID])
GO
ALTER TABLE [dbo].[ScoreObservation] CHECK CONSTRAINT [FK_ScoreObservation_Rater]
GO
ALTER TABLE [dbo].[ScoreObservation]  WITH CHECK ADD  CONSTRAINT [FK_ScoreObservation_ScoreRule] FOREIGN KEY([ScoreRuleID])
REFERENCES [dbo].[ScoreRule] ([ScoreRuleID])
GO
ALTER TABLE [dbo].[ScoreObservation] CHECK CONSTRAINT [FK_ScoreObservation_ScoreRule]
GO
ALTER TABLE [dbo].[ScoreObservationEvidence]  WITH CHECK ADD  CONSTRAINT [FK_Artifact] FOREIGN KEY([ArtifactID])
REFERENCES [dbo].[Artifact] ([ArtifactID])
GO
ALTER TABLE [dbo].[ScoreObservationEvidence] CHECK CONSTRAINT [FK_Artifact]
GO
ALTER TABLE [dbo].[ScoreObservationEvidence]  WITH CHECK ADD  CONSTRAINT [FK_ScoreObservation] FOREIGN KEY([ScoreObservationID])
REFERENCES [dbo].[ScoreObservation] ([ScoreObservationID])
GO
ALTER TABLE [dbo].[ScoreObservationEvidence] CHECK CONSTRAINT [FK_ScoreObservation]
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot]  WITH CHECK ADD  CONSTRAINT [FK_ScoreObservationSnapshot_Execution] FOREIGN KEY([ExecutionID])
REFERENCES [dbo].[Execution] ([ExecutionID])
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot] CHECK CONSTRAINT [FK_ScoreObservationSnapshot_Execution]
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot]  WITH CHECK ADD  CONSTRAINT [FK_ScoreObservationSnapshot_ExperimentVersion] FOREIGN KEY([ExperimentVersionID])
REFERENCES [dbo].[ExperimentVersion] ([ExperimentVersionID])
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot] CHECK CONSTRAINT [FK_ScoreObservationSnapshot_ExperimentVersion]
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot]  WITH CHECK ADD  CONSTRAINT [FK_ScoreObservationSnapshot_Phase] FOREIGN KEY([PhaseID])
REFERENCES [dbo].[Phase] ([PhaseID])
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot] CHECK CONSTRAINT [FK_ScoreObservationSnapshot_Phase]
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot]  WITH CHECK ADD  CONSTRAINT [FK_ScoreObservationSnapshot_ScoreCategory] FOREIGN KEY([ScoreCategoryID])
REFERENCES [dbo].[ScoreCategory] ([ScoreCategoryID])
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot] CHECK CONSTRAINT [FK_ScoreObservationSnapshot_ScoreCategory]
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot]  WITH CHECK ADD  CONSTRAINT [FK_ScoreObservationSnapshot_ScoreObservation] FOREIGN KEY([ScoreObservationID])
REFERENCES [dbo].[ScoreObservation] ([ScoreObservationID])
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot] CHECK CONSTRAINT [FK_ScoreObservationSnapshot_ScoreObservation]
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot]  WITH CHECK ADD  CONSTRAINT [FK_ScoreObservationSnapshot_ScoreRule] FOREIGN KEY([ScoreRuleID])
REFERENCES [dbo].[ScoreRule] ([ScoreRuleID])
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot] CHECK CONSTRAINT [FK_ScoreObservationSnapshot_ScoreRule]
GO
ALTER TABLE [dbo].[ScoreRule]  WITH CHECK ADD  CONSTRAINT [FK_ScoreRule_ExperimentVersion] FOREIGN KEY([ExperimentVersionID])
REFERENCES [dbo].[ExperimentVersion] ([ExperimentVersionID])
GO
ALTER TABLE [dbo].[ScoreRule] CHECK CONSTRAINT [FK_ScoreRule_ExperimentVersion]
GO
ALTER TABLE [dbo].[ScoreRule]  WITH CHECK ADD  CONSTRAINT [FK_ScoreRule_Phase] FOREIGN KEY([PhaseID])
REFERENCES [dbo].[Phase] ([PhaseID])
GO
ALTER TABLE [dbo].[ScoreRule] CHECK CONSTRAINT [FK_ScoreRule_Phase]
GO
ALTER TABLE [dbo].[ScoreRule]  WITH CHECK ADD  CONSTRAINT [FK_ScoreRule_ScoreCategory] FOREIGN KEY([ScoreCategoryID])
REFERENCES [dbo].[ScoreCategory] ([ScoreCategoryID])
GO
ALTER TABLE [dbo].[ScoreRule] CHECK CONSTRAINT [FK_ScoreRule_ScoreCategory]
GO
ALTER TABLE [dbo].[Artifact]  WITH CHECK ADD  CONSTRAINT [artifact-bytelength-greater-equal-zero] CHECK  ((datalength([ByteLength])>=(0)))
GO
ALTER TABLE [dbo].[Artifact] CHECK CONSTRAINT [artifact-bytelength-greater-equal-zero]
GO
ALTER TABLE [dbo].[Artifact]  WITH CHECK ADD  CONSTRAINT [Chk_SHA256_Hex] CHECK  ((NOT ([SHA256]) collate Latin1_General_100_BIN2 like '%[^0-9A-Fa-f]%'))
GO
ALTER TABLE [dbo].[Artifact] CHECK CONSTRAINT [Chk_SHA256_Hex]
GO
ALTER TABLE [dbo].[Artifact]  WITH CHECK ADD  CONSTRAINT [CK_SHA256_Exact_Length] CHECK  ((datalength([SHA256])=(64)))
GO
ALTER TABLE [dbo].[Artifact] CHECK CONSTRAINT [CK_SHA256_Exact_Length]
GO
ALTER TABLE [dbo].[Execution]  WITH CHECK ADD  CONSTRAINT [attempt_number_greater_than_zero] CHECK  (([AttemptNumber]>(0)))
GO
ALTER TABLE [dbo].[Execution] CHECK CONSTRAINT [attempt_number_greater_than_zero]
GO
ALTER TABLE [dbo].[Execution]  WITH CHECK ADD  CONSTRAINT [endutc_is_null_or_greaterequal_startutc] CHECK  (([EndUTC] IS NULL OR [EndUTC]>=[StartUTC]))
GO
ALTER TABLE [dbo].[Execution] CHECK CONSTRAINT [endutc_is_null_or_greaterequal_startutc]
GO
ALTER TABLE [dbo].[Execution]  WITH CHECK ADD  CONSTRAINT [interventioncount_greater_equal_zero] CHECK  (([InterventionCount]>=(0)))
GO
ALTER TABLE [dbo].[Execution] CHECK CONSTRAINT [interventioncount_greater_equal_zero]
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot]  WITH CHECK ADD  CONSTRAINT [CK_ScoreObservationSnapshot_MinScore_LT_MaxScore] CHECK  (([MinScore]<[MaxScore]))
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot] CHECK CONSTRAINT [CK_ScoreObservationSnapshot_MinScore_LT_MaxScore]
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot]  WITH CHECK ADD  CONSTRAINT [CK_ScoreObservationSnapshot_ObservedScore_InRange] CHECK  (([ObservedScore]>=[MinScore] AND [ObservedScore]<=[MaxScore]))
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot] CHECK CONSTRAINT [CK_ScoreObservationSnapshot_ObservedScore_InRange]
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot]  WITH CHECK ADD  CONSTRAINT [CK_ScoreObservationSnapshot_WeightPercent] CHECK  (([WeightPercent]>=(0) AND [WeightPercent]<=(100)))
GO
ALTER TABLE [dbo].[ScoreObservationSnapshot] CHECK CONSTRAINT [CK_ScoreObservationSnapshot_WeightPercent]
GO
ALTER TABLE [dbo].[ScoreRule]  WITH CHECK ADD  CONSTRAINT [scorerule_minscore_less_maxscore] CHECK  (([MinScore]<[MaxScore]))
GO
ALTER TABLE [dbo].[ScoreRule] CHECK CONSTRAINT [scorerule_minscore_less_maxscore]
GO
ALTER TABLE [dbo].[ScoreRule]  WITH CHECK ADD  CONSTRAINT [scorerule_weightpercent_between_0_100] CHECK  (([WeightPercent]>=(0) AND [WeightPercent]<=(100)))
GO
ALTER TABLE [dbo].[ScoreRule] CHECK CONSTRAINT [scorerule_weightpercent_between_0_100]
GO
/****** Object:  StoredProcedure [dbo].[usp_AcquireVersionLock]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- The primitive controlled-write procedures call first, inside their own transaction:
--   SET XACT_ABORT ON; BEGIN TRAN; EXEC dbo.usp_AcquireVersionLock ...; <write>; COMMIT;
-- Reports whether the version exists and its LockedUTC; the caller decides what to do with that.
-- With XACT_ABORT ON, a lock-wait timeout (1222) or deadlock (1205) rolls the whole transaction back.
CREATE   PROCEDURE [dbo].[usp_AcquireVersionLock]
    @ExperimentVersionID bigint,
    @Exclusive bit = 0,
    @Found bit OUTPUT,
    @LockedUTC datetime2(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @@TRANCOUNT = 0
        THROW 50000, N'usp_AcquireVersionLock must be called inside a transaction so the lock is held.', 1;

    SET @Found = 0;
    SET @LockedUTC = NULL;

    IF @Exclusive = 1
        SELECT @Found = 1, @LockedUTC = LockedUTC
        FROM dbo.ExperimentVersion WITH (XLOCK, HOLDLOCK, ROWLOCK)
        WHERE ExperimentVersionID = @ExperimentVersionID;
    ELSE
        SELECT @Found = 1, @LockedUTC = LockedUTC
        FROM dbo.ExperimentVersion WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
        WHERE ExperimentVersionID = @ExperimentVersionID;
END

GO
/****** Object:  StoredProcedure [dbo].[usp_AddEvidence]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- An evidence link joins an observation to an artifact, so two versions are involved (normally the same one; the
-- service requires the same Execution, the database does not). Both are locked, in ascending id order, through
-- usp_LockVersions, so a link that spans versions cannot take its second lock out of order inside the trigger.
CREATE   PROCEDURE [dbo].[usp_AddEvidence]
    @ScoreObservationID bigint,
    @ArtifactID bigint,
    @EvidenceNotes nvarchar(500) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_AddEvidence must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @observationVersion bigint = (SELECT ExperimentVersionID FROM dbo.vOwnerScoreObservation WHERE ScoreObservationID = @ScoreObservationID);
    DECLARE @artifactVersion bigint = (SELECT ExperimentVersionID FROM dbo.vOwnerArtifact WHERE ArtifactID = @ArtifactID);
    IF @observationVersion IS NULL OR @artifactVersion IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @Versions dbo.VersionIdList;
    INSERT INTO @Versions (ExperimentVersionID) VALUES (@observationVersion);
    IF @artifactVersion <> @observationVersion
        INSERT INTO @Versions (ExperimentVersionID) VALUES (@artifactVersion);

    EXEC dbo.usp_LockVersions @Versions = @Versions;

    IF NOT EXISTS (SELECT 1 FROM dbo.ScoreObservation WHERE ScoreObservationID = @ScoreObservationID)
       OR NOT EXISTS (SELECT 1 FROM dbo.Artifact WHERE ArtifactID = @ArtifactID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    -- Throws 50100 if either version is locked.
    EXEC dbo.usp_AssertVersionsUnlocked @Versions = @Versions;

    INSERT INTO dbo.ScoreObservationEvidence (ScoreObservationID, ArtifactID, EvidenceNotes)
    VALUES (@ScoreObservationID, @ArtifactID, @EvidenceNotes);

    COMMIT TRANSACTION;
END

GO
/****** Object:  StoredProcedure [dbo].[usp_AssertDesignNotFrozen]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Rejects the statement (error 50400, design-frozen) if any listed version has an Execution.
-- The caller must already hold the version locks (usp_AssertVersionsUnlocked / usp_LockVersions).
CREATE   PROCEDURE [dbo].[usp_AssertDesignNotFrozen]
    @Versions dbo.VersionIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @frozen nvarchar(max) =
    (
        SELECT STRING_AGG(CAST(v.ExperimentVersionID AS nvarchar(20)), N', ') WITHIN GROUP (ORDER BY v.ExperimentVersionID)
        FROM @Versions AS v
        WHERE dbo.fn_VersionHasExecutions(v.ExperimentVersionID) = 1
    );

    IF @frozen IS NOT NULL
    BEGIN
        DECLARE @message nvarchar(2048) = CONCAT(
            N'The design of ExperimentVersion ', @frozen,
            N' is frozen because it has Executions; its Iterations, Parameters, ScoreRules and descriptive fields can no longer change.');
        THROW 50400, @message, 1;
    END
END

GO
/****** Object:  StoredProcedure [dbo].[usp_AssertDesignWritable]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Shared tail used by the writers below: rejects a locked or frozen version. Expects the lock to be held.
CREATE   PROCEDURE [dbo].[usp_AssertDesignWritable]
    @ExperimentVersionID bigint,
    @LockedUTC datetime2(3)
AS
BEGIN
    SET NOCOUNT ON;

    IF @LockedUTC IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' is locked; its governed data is immutable through normal operations.');
        THROW 50100, @lockedMessage, 1;
    END

    DECLARE @versions dbo.VersionIdList;
    INSERT INTO @versions (ExperimentVersionID) VALUES (@ExperimentVersionID);
    EXEC dbo.usp_AssertDesignNotFrozen @versions;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_AssertVersionsUnlocked]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Locks the versions (update lock) and rejects the statement if any of them is locked
-- (error 50100, version-locked). Used by the post-lock guard triggers.
CREATE   PROCEDURE [dbo].[usp_AssertVersionsUnlocked]
    @Versions dbo.VersionIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.usp_LockVersions @Versions = @Versions;

    DECLARE @locked nvarchar(max) =
    (
        SELECT STRING_AGG(CAST(ev.ExperimentVersionID AS nvarchar(20)), N', ') WITHIN GROUP (ORDER BY ev.ExperimentVersionID)
        FROM dbo.ExperimentVersion AS ev
        JOIN @Versions AS v ON v.ExperimentVersionID = ev.ExperimentVersionID
        WHERE ev.LockedUTC IS NOT NULL
    );

    IF @locked IS NOT NULL
    BEGIN
        DECLARE @message nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @locked, N' is locked; its governed data is immutable through normal operations.');
        THROW 50100, @message, 1;
    END
END

GO
/****** Object:  StoredProcedure [dbo].[usp_CreateArtifact]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_CreateArtifact]
    @ExecutionID bigint,
    @ArtifactTypeID int,
    @OriginalName nvarchar(200),
    @OriginalURI nvarchar(2048),
    @StoredURI nvarchar(2048),
    @MimeType nvarchar(100),
    @ByteLength bigint,
    @SHA256 varchar(64),
    @ReceivedUTC datetime2(3),
    @StoredUTC datetime2(3) = NULL,
    @ArtifactID bigint OUTPUT,
    @ExecutionFound bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_CreateArtifact must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ArtifactID = NULL;
    SET @ExecutionFound = 0;
    BEGIN TRANSACTION;

    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.vOwnerExecution WHERE ExecutionID = @ExecutionID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.Execution WHERE ExecutionID = @ExecutionID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @ExecutionFound = 1;

    IF @lockedUtc IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @versionId, N' is locked; Artifacts cannot be added to Execution ', @ExecutionID, N'.');
        THROW 50100, @lockedMessage, 1;
    END

    INSERT INTO dbo.Artifact
        (ExecutionID, ArtifactTypeID, OriginalName, OriginalURI, StoredURI, MimeType, ByteLength, SHA256, ReceivedUTC, StoredUTC)
    VALUES
        (@ExecutionID, @ArtifactTypeID, @OriginalName, @OriginalURI, @StoredURI, @MimeType, @ByteLength, @SHA256, @ReceivedUTC, @StoredUTC);

    SET @ArtifactID = SCOPE_IDENTITY();
    COMMIT TRANSACTION;
END

GO
/****** Object:  StoredProcedure [dbo].[usp_CreateExecution]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_CreateExecution]
    @IterationID bigint,
    @AgentID int,
    @PhaseID int,
    @StatusID int,
    @StartUTC datetime2(3),
    @EndUTC datetime2(3) = NULL,
    @ModelUsed nvarchar(200) = NULL,
    @InterfaceUsed nvarchar(100) = NULL,
    @InterventionCount int,
    @OutcomeNotes nvarchar(max) = NULL,
    @AttemptNumber int,
    @ExecutionID bigint OUTPUT,
    @IterationFound bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_CreateExecution must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ExecutionID = NULL;
    SET @IterationFound = 0;

    BEGIN TRANSACTION;

    -- An iteration never changes version (identity columns are immutable), so reading it before the lock is safe.
    DECLARE @VersionID bigint = (SELECT ExperimentVersionID FROM dbo.Iteration WHERE IterationID = @IterationID);
    IF @VersionID IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @found bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @VersionID, 0, @found OUTPUT, @lockedUtc OUTPUT;

    -- Deleting an iteration takes the same lock, so re-check now that we hold it.
    IF NOT EXISTS (SELECT 1 FROM dbo.Iteration WHERE IterationID = @IterationID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @IterationFound = 1;

    IF @lockedUtc IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @VersionID, N' is locked; Executions cannot be created.');
        THROW 50100, @lockedMessage, 1;
    END

    DECLARE @ruleCount int, @total decimal(38, 2);
    SELECT @ruleCount = COUNT(*), @total = SUM(WeightPercent)
    FROM dbo.ScoreRule
    WHERE ExperimentVersionID = @VersionID AND PhaseID = @PhaseID;

    IF @ruleCount = 0 OR @total <> 100.00
    BEGIN
        DECLARE @ruleMessage nvarchar(2048) = CONCAT(
            N'ExperimentVersion ', @VersionID, N' phase ', @PhaseID, N' has ', @ruleCount,
            N' ScoreRule(s) totaling ', ISNULL(@total, 0), N'; exactly 100.00 is required to create an Execution.');
        THROW 50200, @ruleMessage, 1;
    END

    INSERT INTO dbo.Execution
        (IterationID, AgentID, PhaseID, StatusID, StartUTC, EndUTC, ModelUsed, InterfaceUsed,
         InterventionCount, OutcomeNotes, AttemptNumber)
    VALUES
        (@IterationID, @AgentID, @PhaseID, @StatusID, @StartUTC, @EndUTC, @ModelUsed, @InterfaceUsed,
         @InterventionCount, @OutcomeNotes, @AttemptNumber);

    SET @ExecutionID = SCOPE_IDENTITY();

    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_CreateIteration]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- -- Iteration ---------------------------------------------------------------------------------

CREATE   PROCEDURE [dbo].[usp_CreateIteration]
    @ExperimentVersionID bigint,
    @Sequence int,
    @IterationLabel nvarchar(100),
    @Prompt nvarchar(max),
    @Scenario nvarchar(100) = NULL,
    @Notes nvarchar(max) = NULL,
    @IterationID bigint OUTPUT,
    @VersionFound bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_CreateIteration must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @IterationID = NULL;
    BEGIN TRANSACTION;

    DECLARE @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @ExperimentVersionID, 0, @VersionFound OUTPUT, @lockedUtc OUTPUT;
    IF @VersionFound = 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    EXEC dbo.usp_AssertDesignWritable @ExperimentVersionID, @lockedUtc;

    INSERT INTO dbo.Iteration (ExperimentVersionID, Sequence, IterationLabel, Prompt, Scenario, Notes)
    VALUES (@ExperimentVersionID, @Sequence, @IterationLabel, @Prompt, @Scenario, @Notes);

    SET @IterationID = SCOPE_IDENTITY();
    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_CreateParameter]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- -- Parameter ---------------------------------------------------------------------------------

CREATE   PROCEDURE [dbo].[usp_CreateParameter]
    @ExperimentVersionID bigint,
    @Name nvarchar(200),
    @Value nvarchar(400) = NULL,
    @Unit nvarchar(50) = NULL,
    @Notes nvarchar(max) = NULL,
    @ParameterID bigint OUTPUT,
    @VersionFound bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_CreateParameter must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ParameterID = NULL;
    BEGIN TRANSACTION;

    DECLARE @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @ExperimentVersionID, 0, @VersionFound OUTPUT, @lockedUtc OUTPUT;
    IF @VersionFound = 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    EXEC dbo.usp_AssertDesignWritable @ExperimentVersionID, @lockedUtc;

    INSERT INTO dbo.Parameter (ExperimentVersionID, Name, Value, Unit, Notes)
    VALUES (@ExperimentVersionID, @Name, @Value, @Unit, @Notes);

    SET @ParameterID = SCOPE_IDENTITY();
    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_CreateScoreObservation]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_CreateScoreObservation]
    @ExecutionID bigint,
    @ScoreRuleID int,
    @RaterID int,
    @ObservedScore decimal(5, 2),
    @ObservedUTC datetime2(3) = NULL,
    @ObservationNotes nvarchar(max) = NULL,
    @ScoreObservationID bigint OUTPUT,
    @ExecutionFound bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_CreateScoreObservation must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ScoreObservationID = NULL;
    SET @ExecutionFound = 0;

    BEGIN TRANSACTION;

    -- An execution never changes version or phase (identity columns are immutable), so reading it before the lock is safe.
    DECLARE @VersionID bigint, @ExecutionPhaseID int;
    SELECT @VersionID = i.ExperimentVersionID, @ExecutionPhaseID = e.PhaseID
    FROM dbo.Execution AS e
    JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
    WHERE e.ExecutionID = @ExecutionID;

    IF @VersionID IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @found bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @VersionID, 0, @found OUTPUT, @lockedUtc OUTPUT;

    -- Re-check now that the lock is held.
    IF NOT EXISTS (SELECT 1 FROM dbo.Execution WHERE ExecutionID = @ExecutionID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @ExecutionFound = 1;

    IF @lockedUtc IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @VersionID, N' is locked; ScoreObservations cannot be created.');
        THROW 50100, @lockedMessage, 1;
    END

    DECLARE @RuleVersionID bigint, @RulePhaseID int, @MinScore int, @MaxScore int;
    SELECT @RuleVersionID = ExperimentVersionID, @RulePhaseID = PhaseID,
           @MinScore = MinScore, @MaxScore = MaxScore
    FROM dbo.ScoreRule
    WHERE ScoreRuleID = @ScoreRuleID;

    IF @RuleVersionID IS NOT NULL
    BEGIN
        IF @RuleVersionID <> @VersionID OR @RulePhaseID <> @ExecutionPhaseID
        BEGIN
            DECLARE @mismatchMessage nvarchar(2048) = CONCAT(
                N'ScoreRule ', @ScoreRuleID, N' (version ', @RuleVersionID, N', phase ', @RulePhaseID,
                N') does not match Execution ', @ExecutionID, N' (version ', @VersionID, N', phase ', @ExecutionPhaseID, N').');
            THROW 50300, @mismatchMessage, 1;
        END

        IF @ObservedScore < @MinScore OR @ObservedScore > @MaxScore
        BEGIN
            DECLARE @rangeMessage nvarchar(2048) = CONCAT(
                N'ObservedScore ', @ObservedScore, N' is outside the range ', @MinScore, N' to ', @MaxScore,
                N' of ScoreRule ', @ScoreRuleID, N'.');
            THROW 50302, @rangeMessage, 1;
        END
    END

    INSERT INTO dbo.ScoreObservation (ExecutionID, ScoreRuleID, RaterID, ObservedScore, ObservedUTC, ObservationNotes)
    VALUES (@ExecutionID, @ScoreRuleID, @RaterID, @ObservedScore, @ObservedUTC, @ObservationNotes);

    SET @ScoreObservationID = SCOPE_IDENTITY();

    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_CreateScoreRule]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_CreateScoreRule]
    @ExperimentVersionID bigint,
    @PhaseID int,
    @ScoreCategoryID int,
    @WeightPercent decimal(5, 2),
    @MinScore int,
    @MaxScore int,
    @Active bit,
    @RuleNotes nvarchar(300) = NULL,
    @ScoreRuleID int OUTPUT,
    @VersionFound bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_CreateScoreRule must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ScoreRuleID = NULL;
    BEGIN TRANSACTION;

    DECLARE @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @ExperimentVersionID, 0, @VersionFound OUTPUT, @lockedUtc OUTPUT;
    IF @VersionFound = 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    EXEC dbo.usp_AssertDesignWritable @ExperimentVersionID, @lockedUtc;

    INSERT INTO dbo.ScoreRule (ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active, RuleNotes)
    VALUES (@ExperimentVersionID, @PhaseID, @ScoreCategoryID, @WeightPercent, @MinScore, @MaxScore, @Active, @RuleNotes);

    SET @ScoreRuleID = SCOPE_IDENTITY();
    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_DeleteIteration]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_DeleteIteration]
    @IterationID bigint,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_DeleteIteration must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.Iteration WHERE IterationID = @IterationID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.Iteration WHERE IterationID = @IterationID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    DELETE FROM dbo.Iteration WHERE IterationID = @IterationID;

    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_DeleteParameter]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_DeleteParameter]
    @ParameterID bigint,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_DeleteParameter must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.Parameter WHERE ParameterID = @ParameterID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.Parameter WHERE ParameterID = @ParameterID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    DELETE FROM dbo.Parameter WHERE ParameterID = @ParameterID;

    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_DeleteScoreRule]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Physically deletes the rule, which frees its (ExperimentVersionID, PhaseID, ScoreCategoryID) combination.
-- Only a version that is unlocked and has no Execution is writable, so no ScoreObservation or snapshot row can
-- reference the rule; the foreign keys from both tables refuse the delete regardless (547).
CREATE   PROCEDURE [dbo].[usp_DeleteScoreRule]
    @ScoreRuleID int,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_DeleteScoreRule must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.ScoreRule WHERE ScoreRuleID = @ScoreRuleID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.ScoreRule WHERE ScoreRuleID = @ScoreRuleID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    DELETE FROM dbo.ScoreRule WHERE ScoreRuleID = @ScoreRuleID;

    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_FinalizeExperimentVersion]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_FinalizeExperimentVersion]
    @ExperimentVersionID bigint,
    @Found bit OUTPUT,
    @LockedUTC datetime2(3) OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_FinalizeExperimentVersion must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    SET @LockedUTC = NULL;

    DECLARE @ids nvarchar(max), @total int, @more nvarchar(40), @msg nvarchar(2048);
    DECLARE @startedTransaction bit = 0;

    BEGIN TRY
        BEGIN TRANSACTION;
        SET @startedTransaction = 1;

        -- 1-2. Exclusive lock, then existence and lock state.
        DECLARE @currentLocked datetime2(3);
        EXEC dbo.usp_AcquireVersionLock @ExperimentVersionID, 1, @Found OUTPUT, @currentLocked OUTPUT;

        IF @Found = 0
        BEGIN
            ROLLBACK TRANSACTION;
            RETURN;
        END

        IF @currentLocked IS NOT NULL
        BEGIN
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' is already locked; it cannot be finalized again.');
            THROW 50100, @msg, 1;
        END

        -- 3. Pre-existing snapshot rows.
        SELECT @total = COUNT(*) FROM dbo.ScoreObservationSnapshot WHERE ExperimentVersionID = @ExperimentVersionID;
        IF @total > 0
        BEGIN
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' already has ', @total,
                N' ScoreObservationSnapshot row(s); finalization will not run over existing snapshots. [Total=', @total, N']');
            THROW 50507, @msg, 1;
        END

        -- Working sets, read under the exclusive lock.
        DECLARE @Execs TABLE (ExecutionID bigint NOT NULL PRIMARY KEY, PhaseID int NOT NULL, EndUTC datetime2(3) NULL);
        INSERT INTO @Execs (ExecutionID, PhaseID, EndUTC)
            SELECT e.ExecutionID, e.PhaseID, e.EndUTC
            FROM dbo.Execution AS e
            JOIN dbo.Iteration AS i ON i.IterationID = e.IterationID
            WHERE i.ExperimentVersionID = @ExperimentVersionID;

        DECLARE @Obs TABLE (ScoreObservationID bigint NOT NULL PRIMARY KEY, ExecutionID bigint NOT NULL, ScoreRuleID int NOT NULL, ObservedScore decimal(5, 2) NOT NULL);
        INSERT INTO @Obs (ScoreObservationID, ExecutionID, ScoreRuleID, ObservedScore)
            SELECT so.ScoreObservationID, so.ExecutionID, so.ScoreRuleID, so.ObservedScore
            FROM dbo.ScoreObservation AS so
            JOIN @Execs AS e ON e.ExecutionID = so.ExecutionID;

        DECLARE @Phases TABLE (PhaseID int NOT NULL PRIMARY KEY, RuleCount int NOT NULL, TotalWeight decimal(38, 2) NOT NULL);
        INSERT INTO @Phases (PhaseID, RuleCount, TotalWeight)
            SELECT PhaseID, COUNT(*), SUM(WeightPercent)
            FROM dbo.ScoreRule
            WHERE ExperimentVersionID = @ExperimentVersionID
            GROUP BY PhaseID;

        -- 4a. The version has Executions.
        IF NOT EXISTS (SELECT 1 FROM @Execs)
        BEGIN
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' has no Executions; there is nothing to finalize.');
            THROW 50500, @msg, 1;
        END

        -- 4b. Every Execution has EndUTC.
        SELECT @total = COUNT(*) FROM @Execs WHERE EndUTC IS NULL;
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(ExecutionID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY ExecutionID)
            FROM (SELECT TOP (50) ExecutionID FROM @Execs WHERE EndUTC IS NULL ORDER BY ExecutionID) AS t;
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' Execution(s) have no EndUTC: ', @ids, @more, N'. [ExecutionIDs=', @ids, N';Total=', @total, N']');
            THROW 50501, @msg, 1;
        END

        -- 4c. Every Execution has at least one observation.
        SELECT @total = COUNT(*) FROM @Execs AS e WHERE NOT EXISTS (SELECT 1 FROM @Obs AS o WHERE o.ExecutionID = e.ExecutionID);
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(ExecutionID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY ExecutionID)
            FROM (SELECT TOP (50) e.ExecutionID FROM @Execs AS e
                  WHERE NOT EXISTS (SELECT 1 FROM @Obs AS o WHERE o.ExecutionID = e.ExecutionID) ORDER BY e.ExecutionID) AS t;
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' Execution(s) have no ScoreObservation: ', @ids, @more, N'. [ExecutionIDs=', @ids, N';Total=', @total, N']');
            THROW 50502, @msg, 1;
        END

        -- 4d. Every Execution's phase is a configured phase.
        SELECT @total = COUNT(*) FROM @Execs AS e WHERE NOT EXISTS (SELECT 1 FROM @Phases AS p WHERE p.PhaseID = e.PhaseID);
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(ExecutionID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY ExecutionID)
            FROM (SELECT TOP (50) e.ExecutionID FROM @Execs AS e
                  WHERE NOT EXISTS (SELECT 1 FROM @Phases AS p WHERE p.PhaseID = e.PhaseID) ORDER BY e.ExecutionID) AS t;
            DECLARE @phaseIds nvarchar(max) =
            (
                SELECT STRING_AGG(CAST(PhaseID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY PhaseID)
                FROM (SELECT DISTINCT TOP (50) e.PhaseID FROM @Execs AS e
                      WHERE NOT EXISTS (SELECT 1 FROM @Phases AS p WHERE p.PhaseID = e.PhaseID) ORDER BY e.PhaseID) AS t
            );
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' Execution(s) are in phase(s) with no ScoreRule (phase ', @phaseIds, N'): ', @ids, @more,
                N'. [ExecutionIDs=', @ids, N';PhaseIDs=', @phaseIds, N';Total=', @total, N']');
            THROW 50503, @msg, 1;
        END

        -- 4e. Every configured phase has at least one observation.
        SELECT @total = COUNT(*) FROM @Phases AS p
        WHERE NOT EXISTS (SELECT 1 FROM @Obs AS o JOIN @Execs AS e ON e.ExecutionID = o.ExecutionID WHERE e.PhaseID = p.PhaseID);
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(PhaseID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY PhaseID)
            FROM (SELECT TOP (50) p.PhaseID FROM @Phases AS p
                  WHERE NOT EXISTS (SELECT 1 FROM @Obs AS o JOIN @Execs AS e ON e.ExecutionID = o.ExecutionID WHERE e.PhaseID = p.PhaseID)
                  ORDER BY p.PhaseID) AS t;
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' configured phase(s) have no ScoreObservation: ', @ids, @more, N'. [PhaseIDs=', @ids, N';Total=', @total, N']');
            THROW 50504, @msg, 1;
        END

        -- 4f. Every configured phase's rules total exactly 100.00.
        SELECT @total = COUNT(*) FROM @Phases WHERE TotalWeight <> 100.00;
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(PhaseID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY PhaseID)
            FROM (SELECT TOP (50) PhaseID FROM @Phases WHERE TotalWeight <> 100.00 ORDER BY PhaseID) AS t;
            DECLARE @weights nvarchar(max) =
            (
                SELECT STRING_AGG(CONCAT(N'phase ', PhaseID, N' = ', TotalWeight), N'; ') WITHIN GROUP (ORDER BY PhaseID)
                FROM (SELECT TOP (10) PhaseID, TotalWeight FROM @Phases WHERE TotalWeight <> 100.00 ORDER BY PhaseID) AS t
            );
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' configured phase(s) do not have rules totaling exactly 100.00 (', @weights, N'): ', @ids, @more,
                N'. [PhaseIDs=', @ids, N';Total=', @total, N']');
            THROW 50505, @msg, 1;
        END

        -- 5. Observation integrity.
        SELECT @total = COUNT(*)
        FROM @Obs AS o
        JOIN @Execs AS e ON e.ExecutionID = o.ExecutionID
        JOIN dbo.ScoreRule AS r ON r.ScoreRuleID = o.ScoreRuleID
        WHERE r.ExperimentVersionID <> @ExperimentVersionID OR r.PhaseID <> e.PhaseID
           OR o.ObservedScore < r.MinScore OR o.ObservedScore > r.MaxScore;
        IF @total > 0
        BEGIN
            SELECT @ids = STRING_AGG(CAST(ScoreObservationID AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY ScoreObservationID)
            FROM (SELECT TOP (50) o.ScoreObservationID
                  FROM @Obs AS o
                  JOIN @Execs AS e ON e.ExecutionID = o.ExecutionID
                  JOIN dbo.ScoreRule AS r ON r.ScoreRuleID = o.ScoreRuleID
                  WHERE r.ExperimentVersionID <> @ExperimentVersionID OR r.PhaseID <> e.PhaseID
                     OR o.ObservedScore < r.MinScore OR o.ObservedScore > r.MaxScore
                  ORDER BY o.ScoreObservationID) AS t;
            SET @more = CASE WHEN @total > 50 THEN CONCAT(N' (and ', @total - 50, N' more)') ELSE N'' END;
            SET @msg = CONCAT(N'ExperimentVersion ', @ExperimentVersionID, N' cannot be finalized: ', @total,
                N' ScoreObservation(s) have a ScoreRule of another version or phase, or a score outside the rule''s range: ',
                @ids, @more, N'. [ScoreObservationIDs=', @ids, N';Total=', @total, N']');
            THROW 50506, @msg, 1;
        END

        -- 6-7. Snapshot, then lock, with one database-generated timestamp.
        DECLARE @now datetime2(3) = SYSUTCDATETIME();

        EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = @ExperimentVersionID;

        INSERT INTO dbo.ScoreObservationSnapshot
            (ExperimentVersionID, ExecutionID, PhaseID, ScoreCategoryID, ScoreRuleID, ScoreObservationID,
             ObservedScore, MinScore, MaxScore, WeightPercent, HigherIsBetter, SnapshotUTC)
        SELECT @ExperimentVersionID, e.ExecutionID, e.PhaseID, r.ScoreCategoryID, r.ScoreRuleID, o.ScoreObservationID,
               o.ObservedScore, r.MinScore, r.MaxScore, r.WeightPercent, c.HigherIsBetter, @now
        FROM @Obs AS o
        JOIN @Execs AS e ON e.ExecutionID = o.ExecutionID
        JOIN dbo.ScoreRule AS r ON r.ScoreRuleID = o.ScoreRuleID
        JOIN dbo.ScoreCategory AS c ON c.ScoreCategoryID = r.ScoreCategoryID;

        UPDATE dbo.ExperimentVersion SET LockedUTC = @now WHERE ExperimentVersionID = @ExperimentVersionID;

        -- The token has done its job; clear it before COMMIT (the CATCH block clears it on every failure path).
        EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = NULL;

        -- 8. Commit.
        COMMIT TRANSACTION;
        SET @LockedUTC = @now;
    END TRY
    BEGIN CATCH
        -- The token is session state, not transactional: always clear it so a pooled connection never keeps it.
        EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = NULL;

        -- Roll back only a transaction this procedure started (the standalone check above makes that always the case).
        IF @startedTransaction = 1 AND @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        SET @LockedUTC = NULL;
        THROW;
    END CATCH
END

GO
/****** Object:  StoredProcedure [dbo].[usp_LockVersions]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Locks every listed version in ascending id order, so concurrent multi-version statements
-- acquire locks in the same order and cannot deadlock each other.
CREATE   PROCEDURE [dbo].[usp_LockVersions]
    @Versions dbo.VersionIdList READONLY,
    @Exclusive bit = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF @@TRANCOUNT = 0
        THROW 50000, N'usp_LockVersions must be called inside a transaction so the lock is held.', 1;

    DECLARE @id bigint = (SELECT MIN(ExperimentVersionID) FROM @Versions);
    DECLARE @held bigint;

    WHILE @id IS NOT NULL
    BEGIN
        IF @Exclusive = 1
            SELECT @held = ExperimentVersionID FROM dbo.ExperimentVersion WITH (XLOCK, HOLDLOCK, ROWLOCK)
            WHERE ExperimentVersionID = @id;
        ELSE
            SELECT @held = ExperimentVersionID FROM dbo.ExperimentVersion WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE ExperimentVersionID = @id;

        SET @id = (SELECT MIN(ExperimentVersionID) FROM @Versions WHERE ExperimentVersionID > @id);
    END
END

GO
/****** Object:  StoredProcedure [dbo].[usp_RemoveEvidence]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_RemoveEvidence]
    @ScoreObservationID bigint,
    @ArtifactID bigint,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_RemoveEvidence must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @observationVersion bigint, @artifactVersion bigint;
    SELECT @observationVersion = ObservationVersionID, @artifactVersion = ArtifactVersionID
    FROM dbo.vOwnerScoreObservationEvidence
    WHERE ScoreObservationID = @ScoreObservationID AND ArtifactID = @ArtifactID;

    IF @observationVersion IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @Versions dbo.VersionIdList;
    INSERT INTO @Versions (ExperimentVersionID) VALUES (@observationVersion);
    IF @artifactVersion <> @observationVersion
        INSERT INTO @Versions (ExperimentVersionID) VALUES (@artifactVersion);

    EXEC dbo.usp_LockVersions @Versions = @Versions;

    -- Re-check under the lock: another remover may have taken the link in the meantime.
    IF NOT EXISTS (SELECT 1 FROM dbo.ScoreObservationEvidence WHERE ScoreObservationID = @ScoreObservationID AND ArtifactID = @ArtifactID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertVersionsUnlocked @Versions = @Versions;

    DELETE FROM dbo.ScoreObservationEvidence WHERE ScoreObservationID = @ScoreObservationID AND ArtifactID = @ArtifactID;

    COMMIT TRANSACTION;
END

GO
/****** Object:  StoredProcedure [dbo].[usp_UpdateExecutionRunFacts]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Replaces the full set of run-facts. They remain mutable only until EndUTC is set (docs/ARCHITECTURE.md §5.1); the
-- check is made here, under the lock, so a concurrent update that sets EndUTC cannot be overwritten.
CREATE   PROCEDURE [dbo].[usp_UpdateExecutionRunFacts]
    @ExecutionID bigint,
    @ModelUsed nvarchar(200) = NULL,
    @InterfaceUsed nvarchar(100) = NULL,
    @InterventionCount int,
    @OutcomeNotes nvarchar(max) = NULL,
    @EndUTC datetime2(3) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_UpdateExecutionRunFacts must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    -- An Execution never changes version (identity columns are immutable), so reading its owner before the lock is safe.
    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.vOwnerExecution WHERE ExecutionID = @ExecutionID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    -- Re-read under the lock: this is the state the restriction is evaluated against.
    DECLARE @currentEnd datetime2(3), @exists bit = 0;
    SELECT @exists = 1, @currentEnd = EndUTC FROM dbo.Execution WHERE ExecutionID = @ExecutionID;
    IF @exists = 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    IF @lockedUtc IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @versionId, N' is locked; Execution ', @ExecutionID, N' cannot be changed.');
        THROW 50100, @lockedMessage, 1;
    END

    IF @currentEnd IS NOT NULL
    BEGIN
        DECLARE @endedMessage nvarchar(2048) =
            CONCAT(N'Execution ', @ExecutionID, N' completed at ', CONVERT(nvarchar(30), @currentEnd, 126),
                   N'; its run-facts are immutable through normal ShepherdAPI operations.');
        THROW 50201, @endedMessage, 1;
    END

    UPDATE dbo.Execution
    SET ModelUsed = @ModelUsed, InterfaceUsed = @InterfaceUsed, InterventionCount = @InterventionCount,
        OutcomeNotes = @OutcomeNotes, EndUTC = @EndUTC
    WHERE ExecutionID = @ExecutionID;

    COMMIT TRANSACTION;
END

GO
/****** Object:  StoredProcedure [dbo].[usp_UpdateExperimentVersion]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_UpdateExperimentVersion]
    @ExperimentVersionID bigint,
    @Name nvarchar(200),
    @VersionLabel nvarchar(50),
    @EffectiveDateUTC datetime2(3),
    @Description nvarchar(max),
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_UpdateExperimentVersion must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @ExperimentVersionID, 0, @Found OUTPUT, @lockedUtc OUTPUT;
    IF @Found = 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    EXEC dbo.usp_AssertDesignWritable @ExperimentVersionID, @lockedUtc;

    UPDATE dbo.ExperimentVersion
    SET Name = @Name, VersionLabel = @VersionLabel, EffectiveDateUTC = @EffectiveDateUTC, Description = @Description
    WHERE ExperimentVersionID = @ExperimentVersionID;

    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_UpdateIteration]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_UpdateIteration]
    @IterationID bigint,
    @Sequence int,
    @IterationLabel nvarchar(100),
    @Prompt nvarchar(max),
    @Scenario nvarchar(100) = NULL,
    @Notes nvarchar(max) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_UpdateIteration must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    -- An iteration never changes version (identity columns are immutable), so reading it before the lock is safe.
    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.Iteration WHERE IterationID = @IterationID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    -- Deleting the iteration takes the same lock, so re-check now that we hold it.
    IF NOT EXISTS (SELECT 1 FROM dbo.Iteration WHERE IterationID = @IterationID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    UPDATE dbo.Iteration
    SET Sequence = @Sequence, IterationLabel = @IterationLabel, Prompt = @Prompt, Scenario = @Scenario, Notes = @Notes
    WHERE IterationID = @IterationID;

    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_UpdateParameter]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_UpdateParameter]
    @ParameterID bigint,
    @Name nvarchar(200),
    @Value nvarchar(400) = NULL,
    @Unit nvarchar(50) = NULL,
    @Notes nvarchar(max) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_UpdateParameter must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.Parameter WHERE ParameterID = @ParameterID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    IF NOT EXISTS (SELECT 1 FROM dbo.Parameter WHERE ParameterID = @ParameterID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    UPDATE dbo.Parameter
    SET Name = @Name, Value = @Value, Unit = @Unit, Notes = @Notes
    WHERE ParameterID = @ParameterID;

    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_UpdateScoreObservationContent]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[usp_UpdateScoreObservationContent]
    @ScoreObservationID bigint,
    @ObservedScore decimal(5, 2),
    @ObservedUTC datetime2(3) = NULL,
    @ObservationNotes nvarchar(max) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_UpdateScoreObservationContent must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;

    BEGIN TRANSACTION;

    DECLARE @VersionID bigint =
        (SELECT ExperimentVersionID FROM dbo.vOwnerScoreObservation WHERE ScoreObservationID = @ScoreObservationID);

    IF @VersionID IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @found2 bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @VersionID, 0, @found2 OUTPUT, @lockedUtc OUTPUT;

    DECLARE @ScoreRuleID int, @MinScore int, @MaxScore int;
    SELECT @ScoreRuleID = so.ScoreRuleID, @MinScore = r.MinScore, @MaxScore = r.MaxScore
    FROM dbo.ScoreObservation AS so
    JOIN dbo.ScoreRule AS r ON r.ScoreRuleID = so.ScoreRuleID
    WHERE so.ScoreObservationID = @ScoreObservationID;

    IF @ScoreRuleID IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    IF @lockedUtc IS NOT NULL
    BEGIN
        DECLARE @lockedMessage nvarchar(2048) =
            CONCAT(N'ExperimentVersion ', @VersionID, N' is locked; ScoreObservations cannot be changed.');
        THROW 50100, @lockedMessage, 1;
    END

    IF @ObservedScore < @MinScore OR @ObservedScore > @MaxScore
    BEGIN
        DECLARE @rangeMessage nvarchar(2048) = CONCAT(
            N'ObservedScore ', @ObservedScore, N' is outside the range ', @MinScore, N' to ', @MaxScore,
            N' of ScoreRule ', @ScoreRuleID, N'.');
        THROW 50302, @rangeMessage, 1;
    END

    UPDATE dbo.ScoreObservation
    SET ObservedScore = @ObservedScore, ObservedUTC = @ObservedUTC, ObservationNotes = @ObservationNotes
    WHERE ScoreObservationID = @ScoreObservationID;

    COMMIT TRANSACTION;
END
GO
/****** Object:  StoredProcedure [dbo].[usp_UpdateScoreRule]    Script Date: 10/23/2026 12:49:18 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Changes the non-key configuration only. ExperimentVersionID, PhaseID and ScoreCategoryID are the rule's identity
-- and ownership and never change (trg_ScoreRule_Guard, 50101).
CREATE   PROCEDURE [dbo].[usp_UpdateScoreRule]
    @ScoreRuleID int,
    @WeightPercent decimal(5, 2),
    @MinScore int,
    @MaxScore int,
    @Active bit,
    @RuleNotes nvarchar(300) = NULL,
    @Found bit OUTPUT
AS
BEGIN
    -- Issue #10: controlled-write procedures own their transaction and are called standalone. This must stay the first
    -- statement, before SET XACT_ABORT ON: with XACT_ABORT ON the THROW would doom the caller's transaction.
    IF @@TRANCOUNT > 0
        THROW 50000, N'dbo.usp_UpdateScoreRule must be called outside a transaction; it owns the transaction it begins.', 1;

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Found = 0;
    BEGIN TRANSACTION;

    -- A rule never changes version (identity columns are immutable), so reading it before the lock is safe.
    DECLARE @versionId bigint = (SELECT ExperimentVersionID FROM dbo.ScoreRule WHERE ScoreRuleID = @ScoreRuleID);
    IF @versionId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @versionFound bit, @lockedUtc datetime2(3);
    EXEC dbo.usp_AcquireVersionLock @versionId, 0, @versionFound OUTPUT, @lockedUtc OUTPUT;

    -- Deleting the rule takes the same lock, so re-check now that we hold it.
    IF NOT EXISTS (SELECT 1 FROM dbo.ScoreRule WHERE ScoreRuleID = @ScoreRuleID)
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END
    SET @Found = 1;

    EXEC dbo.usp_AssertDesignWritable @versionId, @lockedUtc;

    UPDATE dbo.ScoreRule
    SET WeightPercent = @WeightPercent, MinScore = @MinScore, MaxScore = @MaxScore, Active = @Active, RuleNotes = @RuleNotes
    WHERE ScoreRuleID = @ScoreRuleID;

    COMMIT TRANSACTION;
END
GO
USE [master]
GO
ALTER DATABASE [Experiments] SET  READ_WRITE 
GO
