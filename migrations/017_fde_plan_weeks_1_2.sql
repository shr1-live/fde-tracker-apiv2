-- The FDE Training Program plan for weeks 1 and 2 (topics, what to cover, practical task and acceptance criteria), as the training sheet gives it.
-- Only the program named 'FDE Training Program' that is not demo data. Idempotent: running again rewrites the same rows.
insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task)
select id, 1, 1, $q$LLM Fundamentals & Model Selection$q$, $q$• LLM architecture - high-level transformer concepts.
• Tokens, tokenization and context windows.
• System prompts, user prompts and assistant messages.
• Temperature and output generation parameters.
• Context length and token limitations.
• Model selection - capability, latency, cost and reliability.
• LLM APIs - request, response, streaming and error handling.
• Multimodal models - text, images and audio overview.
• Input/output token costs and usage tracking.
• Model limitations and hallucinations.$q$, $q$Practical task:
Build a minimal LLM-backed endpoint or service using any technology stack. Run the same fixed prompt set against at least two model options or configurations, where access permits.

Acceptance criteria:
Successful API integration; secure configuration; handles timeout and API errors; captures latency and token usage; compares outputs on a fixed test set; recommends a model with reasons.$q$ from lp_programs where name = 'FDE Training Program' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task)
select id, 1, 2, $q$Advanced Prompt Engineering$q$, $q$• Zero-shot, one-shot and few-shot prompting.
• Role prompting and instruction hierarchy.
• Prompt structure: context, task, constraints and expected output.
• Reasoning-oriented prompting techniques.
• Breaking complex tasks into smaller prompt steps.
• Providing examples and reference data in prompts.
• Prompt templates, variables and reusable components.
• Handling ambiguous requests and asking clarifying questions.
• Reducing prompt injection and instruction-conflict risks.
• Iterating, testing and improving prompts.$q$, $q$Practical task:
Build a recruitment candidate-analysis assistant. Compare at least three prompt versions using the same job description and candidate profiles.

Acceptance criteria:
Consistent output structure; prompt versions are stored; test cases are repeatable; missing information is identified rather than invented; output is grounded in supplied candidate evidence.$q$ from lp_programs where name = 'FDE Training Program' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task)
select id, 1, 3, $q$Structured Outputs & Tool Calling$q$, $q$• Why structured outputs are useful in LLM applications.
• JSON mode and schema-constrained responses.
• Defining and validating JSON schemas.
• Handling malformed, incomplete and unexpected outputs.
• Function/tool calling concepts and workflow.
• Defining tool names, descriptions and input parameters.
• Passing tool results back to the model.
• Multi-step tool calls and orchestration.
• Tool errors, retries, timeouts and fallback handling.
• Tool permissions and safe execution boundaries.$q$, $q$Practical task:
Build an AI task-management assistant that extracts task details into a schema and calls controlled create, list, and update tools. Require confirmation before destructive actions.

Acceptance criteria:
Schema validation; invalid arguments handled; tools execute only in application code; authentication and permission checks; tool failures handled; destructive actions require confirmation; execution is logged.$q$ from lp_programs where name = 'FDE Training Program' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task)
select id, 1, 4, $q$LLM Evaluation, Reliability & Guardrails$q$, $q$• Defining quality criteria and success metrics for LLM features.
• Creating representative test datasets and test cases.
• Evaluating accuracy, relevance, completeness and consistency.
• Using human review and automated evaluation.
• Testing edge cases and adversarial inputs.
• Identifying and reducing hallucinations.
• Input and output validation.
• Guardrails for unsafe, irrelevant or policy-violating responses.
• Monitoring latency, failures, token usage and cost.
• Regression testing prompts and model changes.$q$, $q$Practical task:
Create a 30-case evaluation set for the recruitment assistant: 10 normal, 5 incomplete, 5 ambiguous, 5 adversarial/prompt-injection, and 5 edge cases. Add guardrails and rerun the evaluation.

Acceptance criteria:
All test categories represented; expected behaviour defined; before-and-after results recorded; failure cases documented; prompt-injection tests do not override trusted instructions; no unsupported candidate claims.$q$ from lp_programs where name = 'FDE Training Program' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task)
select id, 1, 5, $q$End-to-End LLM Application$q$, $q$• Understanding the architecture of a basic LLM-powered application.
• Connecting an application to an LLM provider API.
• Managing API keys and environment configuration securely.
• Building the request/response flow from frontend to backend.
• Managing conversation history and context.
• Implementing streaming responses in the user interface.
• Handling loading states, errors, retries and rate limits.
• Logging requests and tracking token usage and cost.
• Testing the complete application workflow.
• Deploying and monitoring a basic LLM application.$q$, $q$Practical task:
Deliver an AI-powered recruitment assistant that analyses supplied candidate profiles against a job description, returns structured, evidence-based output, and uses at least two controlled tools or application functions.

Acceptance criteria:
Working end-to-end flow; structured and validated response; at least two tools/functions; input/output validation; failure handling; basic logs and token/latency tracking; tests, documentation, and a demo.$q$ from lp_programs where name = 'FDE Training Program' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task)
select id, 2, 1, $q$RAG Architecture$q$, $q$• What is Retrieval-Augmented Generation (RAG) and why it is used
• RAG vs. fine-tuning vs. long-context approaches
• High-level RAG architecture and end-to-end data flow
• Document ingestion, preprocessing, extraction (PDF, DOCX, TXT), cleaning and normalization
• Indexing, retrieval, query processing and context construction
• LLM generation using retrieved context
• Source attribution, citations and traceability
• Common RAG patterns, use cases and limitations$q$, $q$Build a basic RAG pipeline using any programming language, framework or platform. Ingest a small set of approved documents and expose a query API or interface.

Acceptance criteria:
• Ingest and process multiple documents
• Keep ingestion, retrieval and generation as clear stages
• Generate answers using retrieved context
• Return source references or citations
• Provide a fallback when the documents do not contain sufficient evidence
• Document the architecture and data flow$q$ from lp_programs where name = 'FDE Training Program' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task)
select id, 2, 2, $q$Chunking & Embeddings$q$, $q$• What document chunks are and why chunking matters
• Fixed-size, token-based, recursive and semantic chunking
• Chunk size and overlap; their impact on retrieval quality
• Structure-aware chunking for headings, tables and sections
• Metadata design and association with source documents
• Embeddings and how text is represented as vectors
• Embedding model selection and trade-offs
• Vector dimensions and similarity concepts
• Generating, storing, reusing and updating embeddings
• Re-embedding strategies when source documents change$q$, $q$Implement at least two chunking strategies and generate embeddings for the same document set. Compare retrieval results using a fixed set of 10-15 questions.

Acceptance criteria:
• Implement two or more configurable chunking strategies
• Support configurable chunk size and overlap where applicable
• Preserve document metadata and source references
• Generate and store embeddings
• Compare retrieval relevance across strategies
• Explain the selected strategy with evidence$q$ from lp_programs where name = 'FDE Training Program' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task)
select id, 2, 3, $q$Vector Search$q$, $q$• What vector databases/stores are and how vector indexes work
• Similarity search and nearest-neighbour concepts
• Cosine similarity, dot product and Euclidean distance
• Top-K retrieval and similarity thresholds
• Metadata filtering and document-level filters
• Semantic search vs. keyword search
• Hybrid search concepts
• Approximate nearest-neighbour (ANN) search
• Reranking and its role in improving relevance
• Retrieval relevance, precision and recall
• Index updates, deletion and synchronization
• Performance and scalability considerations$q$, $q$Implement vector-based retrieval with metadata filters. Test different Top-K values and compare semantic search with keyword or hybrid search where available.

Acceptance criteria:
• Store and retrieve document embeddings
• Perform similarity-based retrieval
• Support Top-K configuration
• Apply metadata filters correctly
• Test a fixed set of representative questions
• Record relevance observations and basic performance results$q$ from lp_programs where name = 'FDE Training Program' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task)
select id, 2, 4, $q$RAG Quality Evaluation$q$, $q$• Why RAG evaluation is needed
• Separating retrieval failures from generation failures
• Groundedness, faithfulness and answer relevance
• Context relevance, context precision and context recall
• Correctness and quality of source citations
• Building representative evaluation datasets
• Measuring retrieval and answer quality
• Evaluation frameworks and LLM-as-a-judge concepts
• Reranking and other retrieval optimization techniques
• Detecting hallucinations and unsupported answers
• Failure analysis, regression testing and iterative improvement
• Latency and cost considerations during evaluation$q$, $q$Create an evaluation set of 15-20 questions with expected answers and relevant source documents. Run the RAG pipeline and evaluate both retrieval and generated answers.

Acceptance criteria:
• Maintain a reusable evaluation dataset
• Evaluate retrieval quality and answer quality separately
• Check groundedness and citation accuracy
• Identify retrieval-related and generation-related failures
• Include questions whose answers are not present in the source documents
• Submit results, failure analysis and improvement notes
• Demonstrate a measurable improvement after at least one optimization$q$ from lp_programs where name = 'FDE Training Program' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task)
select id, 2, 5, $q$RAG Application$q$, $q$• End-to-end RAG application architecture
• Document upload and ingestion workflow
• Query orchestration, retrieval and context assembly
• Prompt construction and answer generation
• Source citations and explainability
• Conversation history and follow-up questions
• Authentication, authorization and access boundaries
• Tenant isolation and document-level security
• Prompt injection and malicious content in documents
• Fallback when evidence is insufficient
• Error handling, logging and observability
• Functional/security testing, deployment and documentation
• Cost, latency and performance considerations$q$, $q$Deliver an end-to-end document assistant using any suitable technology stack. Users should be able to add approved documents, ask questions and receive evidence-based answers with citations.

Acceptance criteria:
• Provide a working document-ingestion and Q&A flow
• Return answers with source citations
• Handle unsupported questions safely
• Enforce appropriate document permissions and access boundaries
• Include error handling and useful logs
• Add functional and security-focused tests
• Submit architecture notes, README, evaluation report and demo
• Explain key design decisions, trade-offs and known limitations independently$q$ from lp_programs where name = 'FDE Training Program' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task;
