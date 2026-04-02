namespace Fyper

module GraphValue =

    /// Universal representation of values returned from any Cypher database.
    /// All drivers normalize their results into this type before result mapping.
    type GraphValue =
        | GNull
        | GBool of bool
        | GInt of int64
        | GFloat of float
        | GString of string
        | GList of GraphValue list
        | GMap of Map<string, GraphValue>
        | GNode of GraphNode
        | GRel of GraphRel
        | GPath of GraphPath

    and GraphNode = {
        Id: int64
        Labels: string list
        Properties: Map<string, GraphValue>
    }

    and GraphRel = {
        Id: int64
        RelType: string
        StartNodeId: int64
        EndNodeId: int64
        Properties: Map<string, GraphValue>
    }

    and GraphPath = {
        Nodes: GraphNode list
        Relationships: GraphRel list
    }

    /// A row of query results
    type GraphRecord = {
        Keys: string list
        Values: Map<string, GraphValue>
    }
