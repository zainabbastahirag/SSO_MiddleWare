# SPOT Report Engine — Accuracy & Density Fix Plan

**Date:** February 19, 2026
**Context:** QA stress tests (Feb 11-12, 2026) revealed critical issues in report accuracy and data density handling. This plan addresses all findings.

---

## Phase 1: Source Hierarchy & Identity Fix (Week 1-2)

**Goal:** Eliminate incorrect identity data caused by CTOS overriding SSM.

### 1.1 Define Document Authority Matrix
- Create a ranked source priority config (not hardcoded, externalised as config/rule file)
  - **Tier 1 (Statutory/Primary):** SSM documents
  - **Tier 2 (Financial/Secondary):** CTOS credit reports
  - **Tier 3 (Supplementary):** All other sources
- For every extractable field (company name, registration number, directors, etc.), map which source tier is authoritative
- When multiple sources provide the same field, the higher-tier source always wins — enforced deterministically, not via prompt

### 1.2 Implement Source Tagging on Ingestion
- Tag every ingested document with its source type and authority tier at the point of upload/parsing
- Ensure the tag propagates through the entire pipeline so downstream logic can reference it
- Add validation: reject or flag documents that cannot be classified into a tier

### 1.3 Build Conflict Resolution Layer
- After extraction, run a **deterministic reconciliation step** that:
  - Groups extracted values by field name
  - Picks the value from the highest-authority source
  - Logs every conflict (field, winning source, losing source, both values) for audit
- This layer sits **between** extraction and report generation — it is not part of the LLM prompt

### 1.4 Acceptance Criteria
- Given SSM says company name is "ABC Sdn Bhd" and CTOS says "ABC Holdings", report must output "ABC Sdn Bhd"
- Conflict log must record the override
- No identity field should ever be sourced from a lower-tier document when a higher-tier document provides it

---

## Phase 2: Hallucination Guardrails (Week 1-2, parallel with Phase 1)

**Goal:** Prevent the AI from fabricating content when source evidence is missing.

### 2.1 Define Minimum Evidence Thresholds Per Section
- For each report section (A, B, C, etc.), define:
  - Which document types are required as input
  - Minimum number of source documents needed to generate that section
- Example: Section B (Strategic Capabilities) requires at least 1 primary source document; if zero are present, skip generation entirely

### 2.2 Implement Pre-Generation Gate
- Before calling the LLM for any report section, check the evidence threshold
- If the threshold is not met:
  - Output a standardised message: **"Insufficient source data to generate this section"**
  - Do **not** call the LLM at all for that section — this is a hard gate, not a prompt instruction
- Log which sections were skipped and why

### 2.3 Add Post-Generation Citation Check
- After the LLM generates a section, validate that every key claim can be traced back to a source document
- Flag any statement that cannot be attributed — these are potential hallucinations
- Decide on a threshold: if more than X% of statements are unattributed, reject the section and fall back to the "Insufficient data" message

### 2.4 Acceptance Criteria
- With zero primary documents for Section B, the system must output "Insufficient source data" — never generated text
- Every factual claim in the report must have a traceable source document reference
- No section should contain inferred or speculated content

---

## Phase 3: Extract-Then-Merge Architecture (Week 3-4)

**Goal:** Fix data density regression and synthesis fatigue by changing the pipeline from single-pass to two-phase.

### 3.1 Build Per-Document Extraction Step
- Process each document **individually** through the LLM (or a specialised extraction model)
- For each document, extract into a **structured schema**:
  - Company identity fields
  - Financial figures (revenue, net loss, etc.) with reporting period
  - Operational data (employee count, branches, etc.)
  - Any section-specific data points
- Store extracted data as structured JSON, not free text
- This eliminates the context window pressure — the LLM only sees one document at a time

### 3.2 Build Deterministic Merge Layer
- Take all per-document structured extractions and merge them:
  - Apply the authority matrix from Phase 1 for conflicting fields
  - For numeric time-series data (e.g., revenue over years), merge into a single timeline
  - For categorical data, deduplicate and consolidate
- Output: one unified structured dataset per report subject

### 3.3 Refactor Report Generation to Use Structured Input
- The final LLM call for narrative generation receives the **merged structured data**, not raw documents
- The prompt becomes: "Given this structured data, write Section X of the report"
- This means the LLM is doing **writing**, not extraction — a much simpler task with lower hallucination risk

### 3.4 Make Output Budget Dynamic
- Remove any fixed `max_tokens` or "keep it concise" instructions from generation prompts
- Scale the output budget based on:
  - Number of data points in the merged structured input
  - Number of reporting periods covered
  - Number of sections that passed the evidence threshold
- Set a **minimum length floor** per section to prevent over-compression

### 3.5 Acceptance Criteria
- A 17-document run must retain all data points that a 10-document run captured (no regression)
- Employee count, exact financial figures, and trend data must never be replaced by generic language
- Report length must scale proportionally with input data volume
- Specific figures (e.g., "net loss of RM 2.3M") must never be replaced by "operational losses"

---

## Phase 4: Data Completeness Validation (Week 4-5)

**Goal:** Automatically detect when a report is missing expected data.

### 4.1 Define Required Fields Per Report Type
- Create a completeness schema that lists mandatory and expected fields for each report type
- Example mandatory fields: company name, registration number, incorporation date, latest revenue figure, employee count

### 4.2 Build Post-Generation Validation Pass
- After report generation, run a validation check against the completeness schema
- For each field:
  - **Present & sourced:** Pass
  - **Present but unsourced:** Warning (potential hallucination)
  - **Missing but source data exists:** Fail (data was dropped)
  - **Missing and no source data:** Info (legitimately unavailable)
- Generate a completeness score (percentage of expected fields populated)

### 4.3 Set Quality Thresholds
- Define minimum completeness score required for a report to be considered valid
- Reports below threshold should be flagged for review, not silently delivered
- Log all field-level validation results for debugging

### 4.4 Acceptance Criteria
- Any field present in source documents must appear in the final report (zero tolerance for data dropout)
- Completeness score is generated and logged for every report
- Reports below the quality threshold are flagged before delivery

---

## Phase 5: Regression Test Suite (Week 5, ongoing)

**Goal:** Prevent these issues from recurring.

### 5.1 Convert QA Stress Test Cases into Automated Tests
- From the Feb 11-12 findings, create test cases for:
  - **Source priority:** SSM vs CTOS conflict scenarios
  - **Hallucination:** Zero-document and low-document scenarios for each section
  - **Data density:** 4-doc, 10-doc, 17-doc incremental runs with expected field checks
  - **Regression:** Verify specific fields (employee count, net loss) survive at all document volumes

### 5.2 Build a Golden Dataset
- Curate a set of test documents with known expected outputs
- Run the full pipeline against this dataset on every code change
- Compare outputs field-by-field against expected values

### 5.3 Integrate into CI/CD
- All regression tests run automatically on every pull request
- Block merge if any accuracy or completeness test fails
- Generate a diff report showing what changed in output between builds

### 5.4 Acceptance Criteria
- Test suite covers all issues found in the Feb 11-12 QA round
- CI pipeline blocks deployment if any test fails
- New QA findings are added as test cases within 48 hours of discovery

---

## Summary Timeline

| Week | Phase | Deliverable |
|------|-------|-------------|
| 1-2 | Phase 1 + 2 (parallel) | Source hierarchy config, conflict resolution layer, hallucination gates |
| 3-4 | Phase 3 + 4 (parallel) | Extract-then-merge pipeline, dynamic output budget, completeness validation |
| 5 | Phase 5 | Full regression test suite in CI/CD |

## Dependencies & Risks

- **Phase 3 is the largest effort** — it is an architectural change to the pipeline, not a prompt fix. Allocate accordingly.
- **Phases 1 and 2 are quick wins** — they can ship independently and immediately reduce risk while Phase 3 is in progress.
- **Phase 5 depends on having golden test data** — start curating test documents in Week 1 so they are ready by Week 5.
- **Coordinate with QA** — they should re-run the original stress tests after each phase ships to validate the fix.
