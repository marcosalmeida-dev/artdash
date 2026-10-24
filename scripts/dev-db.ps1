<#
.SYNOPSIS
    Cria (ou atualiza) o banco de desenvolvimento ArtDash_Dev e o login artdash_dev na instância do
    SQL Server que já roda na máquina (padrão: localhost,1433).

.DESCRIPTION
    Executa docker/mssql/init.sql (idempotente) com um administrador da instância: autenticação do
    Windows por padrão, ou um login SQL com -AdminUser (a senha é pedida se não for informada).
    As tabelas e os dados de exemplo são criados pela aplicação na primeira execução (F5 no Visual
    Studio ou dotnet run --project src/ArtDash.UI).

    Banco, login e senha padrão são os da connection string de src/ArtDash.UI/appsettings.Development.json.
    Se mudar aqui (-Database, -Login, -Password), mude lá também.

.PARAMETER Server
    Instância do SQL Server. Padrão: localhost,1433.

.PARAMETER AdminUser
    Login SQL com permissão de sysadmin (ex.: sa). Sem ele, usa a autenticação do Windows.

.PARAMETER AdminPassword
    Senha do -AdminUser. Se omitida, é pedida sem eco.

.PARAMETER Reset
    Apaga o banco (TODOS os dados) e recria vazio. Pede confirmação.

.PARAMETER Force
    Com -Reset, não pede confirmação (para rodar sem terminal interativo).

.EXAMPLE
    ./scripts/dev-db.ps1
.EXAMPLE
    ./scripts/dev-db.ps1 -AdminUser sa
.EXAMPLE
    ./scripts/dev-db.ps1 -Reset
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string] $Server = 'localhost,1433',
    [string] $AdminUser,
    [string] $AdminPassword,
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')]
    [string] $Database = 'ArtDash_Dev',
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')]
    [string] $Login = 'artdash_dev',
    [string] $Password = 'ArtDash#Dev2026!',
    [switch] $Reset,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$initScript = Join-Path $root 'docker/mssql/init.sql'

# System.Data.SqlClient faz parte do .NET Framework (Windows PowerShell 5.1) e vem com o PowerShell 7:
# não depende de sqlcmd nem de módulos instalados.
function New-ConnectionStringBuilder {
    if (-not ('System.Data.SqlClient.SqlConnection' -as [type])) {
        try { Add-Type -AssemblyName System.Data.SqlClient } catch { }
    }
    if (-not ('System.Data.SqlClient.SqlConnection' -as [type])) {
        throw 'System.Data.SqlClient não está disponível neste PowerShell. Rode pelo Windows PowerShell: powershell -File scripts\dev-db.ps1'
    }
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $Server
    $builder['TrustServerCertificate'] = $true
    $builder['Connect Timeout'] = 10
    $builder['Application Name'] = 'ArtDash dev-db'
    return $builder
}

function Open-SqlConnection([string] $ConnectionString, $Credential) {
    $arguments = @($ConnectionString)
    if ($Credential) { $arguments += $Credential }
    $connection = New-Object System.Data.SqlClient.SqlConnection -ArgumentList $arguments
    # Mostra os PRINT do init.sql.
    $connection.add_InfoMessage({ param($s, $e) Write-Host "  $($e.Message)" })
    $connection.Open()
    return $connection
}

function Invoke-SqlScalar($Connection, [string] $Sql) {
    $command = $Connection.CreateCommand()
    try {
        $command.CommandText = $Sql
        return $command.ExecuteScalar()
    }
    finally {
        $command.Dispose()
    }
}

function Invoke-SqlBatch($Connection, [string] $Sql) {
    $command = $Connection.CreateCommand()
    try {
        $command.CommandText = $Sql
        $command.CommandTimeout = 120
        [void] $command.ExecuteNonQuery()
    }
    finally {
        $command.Dispose()
    }
}

# init.sql no formato do sqlcmd: substitui as variáveis $(Nome) e separa os lotes pelas linhas "GO".
function Get-InitBatches {
    $sql = [IO.File]::ReadAllText($initScript)
    $values = [ordered]@{ AppDb = $Database; AppLogin = $Login; AppPassword = $Password.Replace("'", "''") }
    foreach ($name in $values.Keys) {
        $sql = $sql.Replace('$(' + $name + ')', $values[$name])
    }
    if ($sql -match '\$\((\w+)\)') {
        throw "docker/mssql/init.sql usa a variável $($Matches[1]), que este script não preenche."
    }
    return [regex]::Split($sql, '(?im)^[ \t]*GO[ \t]*(?:--[^\r\n]*)?\r?$') |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ }
}

function Get-SqlError($ErrorRecord) {
    $exception = $ErrorRecord.Exception
    while ($exception -and -not ($exception -is [System.Data.SqlClient.SqlException])) {
        $exception = $exception.InnerException
    }
    return $exception
}

if ($Reset -and $Database -in @('master', 'model', 'msdb', 'tempdb')) {
    throw "Recusado: $Database é um banco do sistema."
}

$admin = New-ConnectionStringBuilder
$admin['Initial Catalog'] = 'master'
$credential = $null
if ($AdminUser) {
    $secret = if ($AdminPassword) { ConvertTo-SecureString $AdminPassword -AsPlainText -Force }
              else { Read-Host "Senha de $AdminUser em $Server" -AsSecureString }
    $secret.MakeReadOnly()
    $credential = New-Object System.Data.SqlClient.SqlCredential -ArgumentList $AdminUser, $secret
    $authentication = "login SQL $AdminUser"
}
else {
    $admin['Integrated Security'] = $true
    $authentication = 'autenticação do Windows'
}

Write-Host "Conectando em $Server ($authentication)..." -ForegroundColor Cyan
try {
    $connection = Open-SqlConnection $admin.ConnectionString $credential
}
catch {
    $sqlError = Get-SqlError $_
    $number = if ($sqlError) { $sqlError.Number } else { 0 }
    if (-not $AdminUser -and $number -in @(18452, 18456)) {
        throw "A instância recusou a autenticação do Windows. Rode com um login SQL de administrador: ./scripts/dev-db.ps1 -AdminUser sa"
    }
    if ($number -eq 18456) {
        throw "Login ou senha de '$AdminUser' inválidos em $Server."
    }
    throw ("Não foi possível conectar em $Server ($($_.Exception.GetBaseException().Message)). " +
        'Confira se o serviço do SQL Server está rodando e se o TCP/IP está habilitado na porta 1433 (SQL Server Configuration Manager).')
}

try {
    $version = Invoke-SqlScalar $connection "SELECT CONCAT(CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')), N' - ', CONVERT(nvarchar(128), SERVERPROPERTY('Edition')))"
    Write-Host "  SQL Server $version"

    if ((Invoke-SqlScalar $connection "SELECT IS_SRVROLEMEMBER('sysadmin')") -ne 1) {
        throw 'Este usuário não é sysadmin da instância (precisa criar banco e login). Use outro, por exemplo -AdminUser sa.'
    }
    if ((Invoke-SqlScalar $connection "SELECT CONVERT(int, SERVERPROPERTY('IsIntegratedSecurityOnly'))") -eq 1) {
        throw ("A instância aceita só autenticação do Windows, e a aplicação entra com o login SQL $Login. " +
            "Habilite o modo misto (SSMS > Propriedades do servidor > Segurança > 'Modo de Autenticação do SQL Server e do Windows'), " +
            'reinicie o serviço do SQL Server e rode o script de novo.')
    }

    if ($Reset) {
        $confirmed = if ($Force -and -not $WhatIfPreference) { $true }
                     else { $PSCmdlet.ShouldProcess("banco $Database em $Server", 'Apagar o banco com TODOS os dados e recriar vazio') }
        if (-not $confirmed) {
            Write-Host 'Nada foi alterado.' -ForegroundColor Yellow
            return
        }
        Invoke-SqlBatch $connection "IF DB_ID(N'$Database') IS NOT NULL BEGIN ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database]; END"
        Write-Host "  Banco $Database apagado." -ForegroundColor Yellow
    }

    Write-Host "Criando/atualizando o banco $Database e o login $Login..." -ForegroundColor Cyan
    foreach ($batch in Get-InitBatches) {
        Invoke-SqlBatch $connection $batch
    }
}
finally {
    $connection.Dispose()
}

# Confere se a aplicação consegue entrar com o próprio login (exige autenticação mista habilitada).
$app = New-ConnectionStringBuilder
$app['Initial Catalog'] = $Database
$app['User ID'] = $Login
$app['Password'] = $Password
try {
    (Open-SqlConnection $app.ConnectionString $null).Dispose()
}
catch {
    throw "O banco e o login foram criados, mas o login $Login não conseguiu entrar: $($_.Exception.GetBaseException().Message)"
}

Write-Host ''
Write-Host "Pronto! Banco $Database em $Server, login $Login (connection string de appsettings.Development.json)." -ForegroundColor Green
Write-Host 'Agora rode a aplicação (cria as tabelas e os dados de exemplo): F5 no Visual Studio ou'
Write-Host '    dotnet run --project src/ArtDash.UI --launch-profile https'
