# Viewer Architecture

> **Status**: Designed, not yet implemented. This document specifies the planned React Flow visualization architecture.

## Technology Stack

- **Canvas**: React Flow (v11+, @xyflow/react)
- **Layout**: Dagre (layered graph layout)
- **State**: Zustand (lightweight state management)
- **Data format**: graph.json from CodeGraphJsonExporter

## Component Tree

```
App
├── Toolbar
│   ├── LayerToggle    (Call / Framework / Data / Transaction)
│   ├── KindFilter     (node Kind: method / entity / table / external)
│   ├── ConfidenceFilter (slider: Low → Exact)
│   ├── SearchBar       (method name, entity name, table name)
│   └── ExportButton
├── GraphCanvas (ReactFlow)
│   ├── CustomNode (per Kind)
│   ├── CustomEdge (per Kind, with label)
│   └── MiniMap
├── Sidebar
│   ├── NodeDetail      (selected node: attributes, facts, edges)
│   ├── PathList        (semantic query results)
│   └── StatisticsPanel
└── Legend
```

## Layer Layout (Dagre)

| Layer | Rank | Color | Nodes |
|-------|------|-------|-------|
| Route | 0 | Blue | entry-point annotated nodes |
| Controller | 1 | Cyan | Controllers calling services |
| Service | 2 | Green | Service/business layer |
| Repository | 3 | Orange | nh:entity-access edge sources |
| Entity | 4 | Red | External entity nodes |
| Table | 5 | Purple | (reserved) table nodes |

Layout algorithm: `dagre.layout(graph, { rankdir: 'LR', ranksep: 100, nodesep: 50 })`

## Path Highlight

When a `SemanticPath` is selected:
- All nodes in path: border highlighted, opacity 1.0
- All nodes NOT in path: opacity 0.2
- Edges in path: stroke width 3, colored by edge kind
- Animation: sequential reveal along path direction

## View Filtering

| Filter | Type | Description |
|--------|------|-------------|
| Kind toggle | Checkbox | Show/hide method/entity/table/external nodes |
| Edge kind toggle | Checkbox | Show/hide call/nh:entity-access/spring:implements |
| Min confidence | Slider | Hide edges below confidence threshold |
| Max depth | Slider | Limit graph expansion depth from selected node |
| Fan-in threshold | Number | Only show nodes with ≥N incoming edges |
| Project filter | Dropdown | Show only nodes from specific .csproj |

## Performance Optimizations

| Technique | Description |
|-----------|-------------|
| Virtualization | ReactFlow built-in viewport culling |
| Lazy loading | Load subgraph on node expand, not entire graph |
| Web Worker | Run Dagre layout in worker thread |
| Memoization | `React.memo` on CustomNode, CustomEdge |
| Edge dedup | Merge parallel edges between same node pair |
| Cluster collapse | Collapse method groups within same class |
| JSON streaming | Load graph.json incrementally for large graphs |

## Data Flow

```
graph.json (static export)
    │
    ▼
useGraphLoader()  →  nodes[], edges[]
    │
    ▼
useLayouter()     →  { x, y } positions
    │
    ▼
useFilteredView() →  filtered nodes + edges
    │
    ▼
ReactFlow canvas
```

## Color Coding by Edge Kind

| Edge Kind | Color | Style |
|-----------|-------|-------|
| `call` | #6366f1 (indigo) | Solid |
| `nh:entity-access` | #f59e0b (amber) | Dashed |
| `spring:implements` | #10b981 (emerald) | Dotted |
| `spring:property-ref` | #8b5cf6 (violet) | Dotted |
