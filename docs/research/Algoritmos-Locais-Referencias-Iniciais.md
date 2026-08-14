# Algoritmos locais — referências iniciais

Data da revisão: 2026-08-14.

Este arquivo registra as referências usadas para orientar `docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md`. Ele não é um contract normativo e não autoriza incorporação de código de terceiros.

## Graph search / routing

- Hart, P. E.; Nilsson, N. J.; Raphael, B. — *A Formal Basis for the Heuristic Determination of Minimum Cost Paths*. IEEE Transactions on Systems Science and Cybernetics, 1968. DOI: `10.1109/TSSC.1968.300136`.
- Hadlock, F. O. — *A shortest path algorithm for grid graphs*. Networks, 1977. DOI: `10.1002/net.3230070404`.
- McMurchie, L. E.; Ebeling, C. — *PathFinder: A Negotiation-Based Performance-Driven Router for FPGAs*. FPGA 1995. Referência para negotiated congestion e histórico de custo de recursos.

## Placement / combinatorial search

- Kirkpatrick, S.; Gelatt, C. D.; Vecchi, M. P. — *Optimization by Simulated Annealing*. Science, 1983. DOI: `10.1126/science.220.4598.671`.
- Shaw, P. — *Using Constraint Programming and Local Search Methods to Solve Vehicle Routing Problems*. CP 1998. DOI: `10.1007/3-540-49481-2_30`. Referência conceitual para Large Neighborhood Search (relax/reoptimize).

## Wirelength / Steiner topology

- Chu, C.; Wong, Y.-C. — *FLUTE: Fast Lookup Table Based Rectilinear Steiner Minimal Tree Algorithm for VLSI Design*. IEEE TCAD, 2008. DOI: `10.1109/TCAD.2007.907068`.

## Spatial indexing

- Guttman, A. — *R-trees: A Dynamic Index Structure for Spatial Searching*. SIGMOD, 1984. DOI: `10.1145/602259.602266`.
- NetTopologySuite API — `Quadtree<T>` suporta insert/query/remove e foi identificado como candidate prático para broad-phase mutável no .NET.

## Computational geometry

- Clipper2 (Angus Johnson) — implementação C#/C++/Delphi de polygon clipping/offsetting com paths de 64 bits. Candidate para `IGeometryKernel` adapter; versão/licença devem ser verificadas no bootstrap antes da adoção.

## Discrete optimization

- Google OR-Tools / CP-SAT — solver de constraint programming/SAT com C# wrapper. Candidate opcional apenas para subproblemas discretos bem delimitados.

## EDA implementation references

- OpenROAD/FastRoute/TritonRoute — referência de arquitetura global routing → guides → detailed routing, resource grids, congestion e pin-access analysis. O domínio é IC/VLSI, portanto conceitos devem ser adaptados e benchmarkados para PCB, não copiados cegamente.
- Freerouting — referência e benchmark de PCB autorouting via Specctra DSN/SES. O repositório consultado declara GPL-3.0; por default é benchmark/reference only conforme ADR-0004.
