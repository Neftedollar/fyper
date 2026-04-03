---
layout: default
title: Apache AGE Driver
parent: Reference
description: "Apache AGE PostgreSQL driver for Fyper. Setup, SQL wrapping, supported features."
nav_order: 5
---

# Apache AGE Driver

## Install

```bash
dotnet add package Fyper.Age
```

## Prerequisites

- PostgreSQL with the Apache AGE extension installed
- A named graph created in AGE

## Setup

```fsharp
open Fyper.Age
open Npgsql

let dataSource = NpgsqlDataSource.Create(
    "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass")
let driver = new AgeDriver(dataSource, graphName = "my_graph")
```

The driver wraps an `NpgsqlDataSource`. Each query opens a connection, runs `LOAD 'age'`, and wraps the Cypher in AGE's SQL function.

## How It Works

Cypher queries are wrapped in AGE's SQL function:

```sql
SELECT * FROM cypher('graph_name', $$ MATCH (n:Person) RETURN n $$) AS (n agtype)
```

Return column aliases are extracted from the RETURN clause. Parameters use AGE's positional `$N` argument syntax.

## Capabilities

Limited feature set (`DriverCapabilities.minimal`):

| Feature | Supported |
|---------|-----------|
| MATCH / WHERE / RETURN | yes |
| CREATE / DELETE / SET | yes |
| ORDER BY / SKIP / LIMIT | yes |
| Variable-length paths | yes |
| OPTIONAL MATCH | **no** |
| MERGE | **no** |
| UNWIND | **no** |
| CASE | **no** |

Unsupported features are rejected at query construction time with `FyperUnsupportedFeatureException`.

## Docker Compose

For testing, use the included `docker-compose.yml`:

```bash
docker compose up -d    # Starts AGE on localhost:5432
docker compose down     # Stop
```
