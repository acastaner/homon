# Archived plans

Plans whose row in `../README.md` reads `DONE` or `REJECTED`. They are kept because a plan records
*why* something is the way it is at a level of detail the code and `docs/ARCHITECTURE.md`
deliberately do not: the alternatives that were weighed, what the executor was told to verify, and
what went wrong on the way. Several of them are cited by name from source comments.

`../README.md` is the index to these and to the plans still outstanding, and it is the authority on
status — a plan's own text says what it intended, not what became of it.

**Paths written inside these files are as they were when the plan ran**, which means a reference to
`plans/002-monitoring-core-groups-and-ping.md` inside an archived plan now resolves one directory
up from where the file actually sits. That is deliberate. A plan is a record of a decision taken at
a particular commit, and rewriting its body to keep a path tidy would quietly turn it into
something nobody actually wrote. Read `plans/NNN-…` inside an archived plan as `plans/archive/NNN-…`.

Live documents — the outstanding plans, the index, and source comments that cite a plan — do point
here directly, and must be kept that way if anything moves again.
