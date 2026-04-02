---
layout: default
title: CE Reference
nav_order: 3
---

# Computation Expression Reference

All operations available inside `cypher { }`.

## Node Binding

```fsharp
for p in node<Person> do       // MATCH (p:Person)
for m in optionalNode<Movie> do // OPTIONAL MATCH (m:Movie)
```

## Filtering

```fsharp
where (p.Age > 30)                           // WHERE p.age > $p0
where (p.Age > 20 && p.Age < 50)             // WHERE (p.age > $p0) AND (p.age < $p1)
where (p.Age < 18 || p.Age > 65)             // WHERE (p.age < $p0) OR (p.age > $p1)
where (p.Name.Contains("Tom"))               // WHERE p.name CONTAINS $p0
where (p.Name.StartsWith("A"))               // WHERE p.name STARTS WITH $p0
where (p.Name.EndsWith("son"))               // WHERE p.name ENDS WITH $p0
where (p.Name = "Alice")                     // WHERE p.name = $p0
```

## Projection

```fsharp
select p                                     // RETURN p
select p.Name                                // RETURN p.name AS name
select (p.Name, m.Title)                     // RETURN p.name AS name, m.title AS title
select {| Age = p.Age; Count = count() |}    // RETURN p.age AS age, count(*) AS count
selectDistinct p.Name                        // RETURN DISTINCT p.name AS name
```

## Sorting and Pagination

```fsharp
orderBy p.Age                    // ORDER BY p.age
orderByDesc p.Age                // ORDER BY p.age DESC
skip 10                          // SKIP $skip_0
limit 5                          // LIMIT $limit_1
```

## Relationships

```fsharp
matchRel (p -- edge<ActedIn> --> m)                    // MATCH (p)-[:ACTED_IN]->(m)
matchPath (p -- edge<Knows> --> q) (Between(1, 5))     // MATCH (p)-[:KNOWS*1..5]->(q)
matchPath (p -- edge<Knows> --> q) AnyLength            // MATCH (p)-[:KNOWS*]->(q)
matchPath (p -- edge<Knows> --> q) (Exactly 3)          // MATCH (p)-[:KNOWS*3]->(q)
```

## Mutations

```fsharp
create { Name = "Tom"; Age = 50 }                       // CREATE (:Person {name: $p0, age: $p1})
createRel (p -- edge<ActedIn> --> m)                     // CREATE (p)-[:ACTED_IN]->(m)
set (fun p -> { p with Age = p.Age + 1 })               // SET p.age = (p.age + $p0)
set (fun p -> { p with Age = 51 })                      // SET p.age = $p0
delete p                                                 // DELETE p
detachDelete p                                           // DETACH DELETE p
merge { Name = "Tom"; Age = 0 }                         // MERGE (:Person {name: $p0, age: $p1})
onMatch (fun p -> { p with Age = 50 })                  // ON MATCH SET p.age = $p0
onCreate (fun p -> { p with Age = 25 })                 // ON CREATE SET p.age = $p0
```

## Advanced

```fsharp
unwind names "name"                                      // UNWIND $p0 AS name
withClause p                                             // WITH p
```

## Aggregation Functions

```fsharp
count()                  // count(*)
countDistinct(p.Name)    // countDistinct(p.name)
sum(p.Age)               // sum(p.age)
avg(p.Age)               // avg(p.age)
collect(p.Name)          // collect(p.name)
cypherMin(p.Age)         // min(p.age)
cypherMax(p.Age)         // max(p.age)
size(p.Name)             // size(p.name)
```

## CASE Expression

```fsharp
caseWhen (p.Age > 18) p.Name "minor"
// CASE WHEN (p.age > $p0) THEN p.name ELSE $p1 END
```

## Closure Variable Capture

All F# values are automatically captured as parameterized values:

```fsharp
let minAge = 25
let nameFilter = "Tom"
let query = cypher {
    for p in node<Person> do
    where (p.Age > minAge && p.Name = nameFilter)
    select p
}
// WHERE (p.age > $p0) AND (p.name = $p1)
// params: { p0: 25, p1: "Tom" }
```
