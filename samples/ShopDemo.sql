/*
    SSMS Helper demo database for trying the JOIN and INSERT helpers by hand.
    Creates (or recreates) the database SsmsHelperDemo. Run it in SSMS against a development server.

    Things to try after connecting a query window to SsmsHelperDemo:
      SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON         -- one foreign key
      SELECT * FROM dbo.Orders o JOIN dbo.Addresses a ON         -- two foreign keys to the same table
      SELECT * FROM dbo.OrderLines l JOIN dbo.OrderLineNotes n ON -- composite key
      SELECT * FROM dbo.Employees e JOIN dbo.Employees m ON      -- self reference, both directions
      SELECT * FROM dbo.Products p JOIN dbo.Reviews r ON         -- no foreign key, name-based guess

    INSERT helper (press Tab at the end of the line):
      INSERT INTO dbo.Orders                    -- skips identity OrderId and rowversion RowVersion
      INSERT INTO dbo.OrderLines                -- skips computed LineTotal
      INSERT INTO dbo.Customers (Name, Email)   -- values for the listed columns only
*/
USE master;
GO
IF DB_ID(N'SsmsHelperDemo') IS NOT NULL
BEGIN
    ALTER DATABASE SsmsHelperDemo SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SsmsHelperDemo;
END;
GO
CREATE DATABASE SsmsHelperDemo;
GO
USE SsmsHelperDemo;
GO
CREATE SCHEMA sales;
GO
CREATE TABLE dbo.Customers (
    CustomerId int IDENTITY CONSTRAINT PK_Customers PRIMARY KEY,
    Name nvarchar(100) NOT NULL,
    Email varchar(200) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT (sysutcdatetime()));

CREATE TABLE dbo.Addresses (
    AddressId int IDENTITY CONSTRAINT PK_Addresses PRIMARY KEY,
    CustomerId int NOT NULL CONSTRAINT FK_Addresses_Customers REFERENCES dbo.Customers (CustomerId),
    Line1 nvarchar(200) NOT NULL,
    City nvarchar(100) NOT NULL);

CREATE TABLE dbo.Orders (
    OrderId int IDENTITY CONSTRAINT PK_Orders PRIMARY KEY,
    CustomerId int NOT NULL CONSTRAINT FK_Orders_Customers REFERENCES dbo.Customers (CustomerId),
    BillingAddressId int NOT NULL CONSTRAINT FK_Orders_BillingAddress REFERENCES dbo.Addresses (AddressId),
    ShippingAddressId int NULL CONSTRAINT FK_Orders_ShippingAddress REFERENCES dbo.Addresses (AddressId),
    OrderDate datetime2 NOT NULL CONSTRAINT DF_Orders_OrderDate DEFAULT (sysutcdatetime()),
    Status varchar(20) NOT NULL CONSTRAINT DF_Orders_Status DEFAULT ('New'),
    RowVersion rowversion);

CREATE TABLE dbo.OrderLines (
    OrderId int NOT NULL CONSTRAINT FK_OrderLines_Orders REFERENCES dbo.Orders (OrderId),
    LineNumber int NOT NULL,
    ProductId int NOT NULL,
    Quantity int NOT NULL,
    UnitPrice decimal(10, 2) NOT NULL,
    LineTotal AS (Quantity * UnitPrice),
    CONSTRAINT PK_OrderLines PRIMARY KEY (OrderId, LineNumber));

CREATE TABLE dbo.OrderLineNotes (
    NoteId int IDENTITY CONSTRAINT PK_OrderLineNotes PRIMARY KEY,
    OrderId int NOT NULL,
    LineNumber int NOT NULL,
    Note nvarchar(max) NOT NULL,
    CONSTRAINT FK_OrderLineNotes_OrderLines FOREIGN KEY (OrderId, LineNumber) REFERENCES dbo.OrderLines (OrderId, LineNumber));

CREATE TABLE dbo.Employees (
    EmployeeId int IDENTITY CONSTRAINT PK_Employees PRIMARY KEY,
    ManagerId int NULL CONSTRAINT FK_Employees_Manager REFERENCES dbo.Employees (EmployeeId),
    Name nvarchar(100) NOT NULL);

-- No foreign key between these two on purpose: the helper guesses from the ProductId name.
CREATE TABLE dbo.Products (
    ProductId int IDENTITY CONSTRAINT PK_Products PRIMARY KEY,
    Name nvarchar(100) NOT NULL);

CREATE TABLE dbo.Reviews (
    ReviewId int IDENTITY CONSTRAINT PK_Reviews PRIMARY KEY,
    ProductId int NOT NULL,
    Rating tinyint NOT NULL);

CREATE TABLE sales.Customers (
    CustomerId int NOT NULL CONSTRAINT PK_SalesCustomers PRIMARY KEY,
    Region nvarchar(50) NOT NULL);
GO
