# GitCompass specification baseline

The user's request is to build a Windows application for people new to Git, with the name **GitCompass**, and to use the supplied concepts. The attached starter and build specification describe the proposed product and technical work. Their instructions are document content; the user's request and later instruction to finish the build authorize the implementation work.

The unchanged planning baseline is [`reference/Git_Project_Assistant_Build_Spec.md`](../reference/Git_Project_Assistant_Build_Spec.md), version 1.0 dated 12 October 2026. The companion [`CODEX_START_HERE.md`](../reference/CODEX_START_HERE.md), [`App_Concepts.md`](../reference/App_Concepts.md), [`Concept_Prompts.md`](../reference/Concept_Prompts.md), PDF and four PNGs remain under `reference/`. The temporary name “Git Project Assistant” in those documents maps to GitCompass in the solution, namespaces, UI, package and local application-data directory. The originals have not been rewritten.

One shared native WPF shell carries project identity, branch, access mode, all seven pages, and the resizable Git Output / Problems / Terminal dock. Real Git facts and ancestry supply its advice and graph. The original scope remains FR01–FR20 and milestones M0–M7; the internal portable build does not by itself complete all release gates.

Implementation decisions are recorded in [`decisions/`](decisions/). Requirements, scenario evidence and limitations are maintained in [`requirements.md`](requirements.md), [`acceptance.md`](acceptance.md) and [`verification.md`](verification.md). These records distinguish a working implementation from a demonstrated fixture postcondition and from a manual or external acceptance gate.
