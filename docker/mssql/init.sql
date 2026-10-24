/*
    ArtDash — banco de desenvolvimento.
    Cria o banco e o login usados pela aplicação (appsettings.Development.json). Idempotente: pode rodar
    quantas vezes quiser. As tabelas NÃO são criadas aqui — a aplicação cria o schema (migrations ou
    EnsureCreated) e os dados de exemplo na primeira execução.

    Executado por scripts/dev-db.ps1 (SQL Server local).
    Para rodar à mão no SSMS, ative Consulta > Modo SQLCMD e defina as variáveis com :setvar.
    Variáveis no formato do sqlcmd:
        $(AppDb)        nome do banco            (padrão ArtDash_Dev)
        $(AppLogin)     login da aplicação       (padrão artdash_dev)
        $(AppPassword)  senha do login
*/

-- Proteção: executado direto no SSMS / VS Code sem o modo SQLCMD, as variáveis não são substituídas e o
-- script criaria um banco e um login com o nome literal da variável. Nesse caso, para aqui sem executar nada.
IF N'$(AppDb)' = N'$' + N'(AppDb)'
BEGIN
    RAISERROR(N'As variáveis do sqlcmd não foram substituídas. Rode scripts/dev-db.ps1 ou ative o modo SQLCMD e defina AppDb, AppLogin e AppPassword com :setvar.', 16, 1);
    SET NOEXEC ON;
END
GO

SET NOCOUNT ON;
GO

IF DB_ID(N'$(AppDb)') IS NULL
BEGIN
    PRINT N'Criando o banco $(AppDb)...';
    CREATE DATABASE [$(AppDb)] COLLATE Latin1_General_100_CI_AI;
END
GO

-- Leitores não bloqueiam escritores (quadro de pedidos atualizando enquanto o motorista envia posições).
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'$(AppDb)' AND is_read_committed_snapshot_on = 1)
BEGIN
    ALTER DATABASE [$(AppDb)] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(AppLogin)')
BEGIN
    PRINT N'Criando o login $(AppLogin)...';
    CREATE LOGIN [$(AppLogin)]
        WITH PASSWORD = N'$(AppPassword)',
             DEFAULT_DATABASE = [$(AppDb)],
             CHECK_POLICY = ON,
             CHECK_EXPIRATION = OFF;
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.sql_logins
                   WHERE name = N'$(AppLogin)' AND PWDCOMPARE(N'$(AppPassword)', password_hash) = 1)
BEGIN
    -- Mantém a senha igual à da connection string de desenvolvimento.
    PRINT N'Atualizando a senha do login $(AppLogin)...';
    ALTER LOGIN [$(AppLogin)] WITH PASSWORD = N'$(AppPassword)';
END
GO

ALTER LOGIN [$(AppLogin)] ENABLE;
GO

-- Só em desenvolvimento: permite que a aplicação recrie o banco se ele for apagado.
ALTER SERVER ROLE [dbcreator] ADD MEMBER [$(AppLogin)];
GO

USE [$(AppDb)];
GO

-- Usuário da aplicação no banco. Se o banco foi criado pela própria aplicação (dbcreator), o login já é
-- o dono (dbo) e não precisa de usuário; se o banco veio de outro servidor, religa o usuário órfão.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE sid = SUSER_SID(N'$(AppLogin)'))
BEGIN
    IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppLogin)')
        ALTER USER [$(AppLogin)] WITH LOGIN = [$(AppLogin)];
    ELSE
        CREATE USER [$(AppLogin)] FOR LOGIN [$(AppLogin)];
END
GO

-- db_owner: a aplicação cria e altera o próprio schema (EnsureCreated / migrations).
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppLogin)' AND type = 'S')
    ALTER ROLE [db_owner] ADD MEMBER [$(AppLogin)];
GO

PRINT N'Banco $(AppDb) pronto. Login: $(AppLogin).';
GO
