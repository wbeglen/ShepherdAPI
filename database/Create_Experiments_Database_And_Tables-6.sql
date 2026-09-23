USE [master]
GO
/****** Object:  Database [Experiments]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[Agent]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[Artifact]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[ArtifactType]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[ETLSourceAudit]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[Execution]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[ExperimentVersion]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[Iteration]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[Parameter]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[Phase]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[Rater]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[ScoreCategory]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[ScoreObservation]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[ScoreObservationEvidence]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[ScoreRule]    Script Date: 9/20/2026 5:34:28 PM ******/
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
/****** Object:  Table [dbo].[Status]    Script Date: 9/20/2026 5:34:28 PM ******/
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
ALTER TABLE [dbo].[ScoreRule]  WITH CHECK ADD  CONSTRAINT [scorerule_minscore_less_maxscore] CHECK  (([MinScore]<[MaxScore]))
GO
ALTER TABLE [dbo].[ScoreRule] CHECK CONSTRAINT [scorerule_minscore_less_maxscore]
GO
ALTER TABLE [dbo].[ScoreRule]  WITH CHECK ADD  CONSTRAINT [scorerule_weightpercent_between_0_100] CHECK  (([WeightPercent]>=(0) AND [WeightPercent]<=(100)))
GO
ALTER TABLE [dbo].[ScoreRule] CHECK CONSTRAINT [scorerule_weightpercent_between_0_100]
GO
USE [master]
GO
ALTER DATABASE [Experiments] SET  READ_WRITE 
GO
