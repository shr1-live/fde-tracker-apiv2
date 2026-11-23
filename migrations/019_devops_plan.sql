-- The DevOps program plan (30 study days: Linux and Bash, Docker, CI/CD and Terraform, Kubernetes and Helm), written day by day
-- from the owner's personal study plan. Only the program named 'Devops' (any capitals) that is not demo data; a no-op if there is none.
-- Day numbers run 1 to 7 inside a week, so the capstone (study days 29 and 30) is week 5. Idempotent: running again rewrites the same rows.

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 1, 1, $q$Linux filesystem, users and permissions$q$, $q$• Linux (Ubuntu): filesystem hierarchy standard (/etc, /var, /proc, /sys)
• User and group permissions (chmod, chown, sudoers)
• KodeKloud DevOps Pre-Requisite course: Linux Basics labs (skip basic Git/YAML lessons)$q$, $q$KodeKloud DevOps Pre-Requisite Course: complete the Linux Basics labs, skipping the basic Git and YAML lessons. In the KodeKloud Linux Playground, practise reading the filesystem layout and setting users, groups and permissions.$q$, $q$KodeKloud: The Linux Basics course (https://app.kodekloud.com/learn/course/the-linux-basics-course/syllabus). Week 1 deliverable: a hardened Linux VM running NGINX with TLS proxying a backend API, supported by an automated maintenance Bash script. About 10 to 12 hours this week.$q$ from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 1, 2, $q$Processes, services, packages and SSH hardening$q$, $q$• Process lifecycle (systemd, systemctl, journalctl)
• Package maintenance via apt
• Resource diagnostics (htop, vmstat, iostat, df)
• SSH hardening$q$, $q$In the KodeKloud Linux Playground, manage a service with systemd (start, enable, read its logs with journalctl), update packages with apt, check CPU, memory, disk and I/O with the diagnostic tools, and apply baseline SSH hardening.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 1, 3, $q$Bash scripting: defensive scripts and flow control$q$, $q$• Defensive scripting (set -euo pipefail)
• Exit code handling
• Flow control and functions (Shell Scripts for Beginners labs)
• Standard pipes and redirection$q$, $q$KodeKloud Shell Scripts for Beginners: complete all flow control, exit codes and functions labs. Write a small script that uses set -euo pipefail, checks exit codes and redirects output to a log file.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 1, 4, $q$Bash scripting: text processing and cron$q$, $q$• Text processing (grep, awk, sed, jq)
• Streams and redirection (Advanced Bash Scripting modules)
• Cron jobs
• The e-commerce project labs from Shell Scripts for Beginners$q$, $q$KodeKloud Shell Scripts for Beginners and Advanced Bash Scripting: complete the e-commerce project labs and the selected modules on awk, sed and streams. Schedule one of your scripts with cron.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 1, 5, $q$Networking: TCP/IP, DNS and TLS$q$, $q$• TCP/IP stack
• DNS lookup and record resolution (dig, nslookup)
• Subnets and CIDR
• HTTP/HTTPS communication and TLS handshakes$q$, $q$KodeKloud DevOps Pre-Requisite Course: complete the Networking Basics labs and the Demystifying DNS course. Use dig and nslookup to resolve records and work out the subnet range for a given CIDR block.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 1, 6, $q$Firewalls and network diagnostics$q$, $q$• Host firewalls (ufw, iptables)
• Diagnostic utilities: curl -v, ss, traceroute, tcpdump$q$, $q$In the KodeKloud Linux Playground, allow only the ports you need with ufw, then use curl -v, ss, traceroute and tcpdump to follow a request and find where a connection fails.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 1, 7, $q$NGINX and the Week 1 milestone lab$q$, $q$• NGINX: reverse proxy architecture
• Upstream load balancing algorithms
• SSL/TLS termination
• Location routing and rate limiting
• Debugging 502 and 504 errors$q$, $q$Week 1 milestone lab. In the KodeKloud Ubuntu Playground, execute baseline SSH hardening, deploy a sample backend service managed by systemd, put NGINX in front as a TLS-terminating reverse proxy, and write an idempotent Bash script for health-checks and log-rotation.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 2, 1, $q$Docker basics and image layers$q$, $q$• Docker course for the absolute beginner: containers, images and the hands-on labs
• Union filesystems
• Image layer caching optimization$q$, $q$KodeKloud Docker Training Course for the Absolute Beginner: fast-track at 1.5x to 2x speed and complete all hands-on container labs.$q$, $q$KodeKloud: Docker Training Course for the Absolute Beginner, Docker Certified Associate (DCA) labs. Week 2 deliverable: a production-grade Dockerized multi-tier application stack running behind an NGINX proxy via Docker Compose. About 10 to 12 hours this week.$q$ from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 2, 2, $q$Production Dockerfiles$q$, $q$• Multi-stage builds
• Minimal base images (Alpine or Distroless)
• Running as a non-root user
• The .dockerignore file$q$, $q$Write a production Dockerfile for a small service: multi-stage build, a minimal base image, a non-root user and a .dockerignore. Compare the image size before and after.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 2, 3, $q$Container networking and volumes$q$, $q$• Bridge and host container networking
• Volume persistence
• Storage drivers (DCA labs)$q$, $q$KodeKloud Docker course and DCA labs: complete the networking and volume labs, then the selected labs on storage drivers.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 2, 4, $q$Image optimization and container security$q$, $q$• Multi-stage builds and image optimization (DCA labs)
• Container security (DCA labs)$q$, $q$KodeKloud Docker Certified Associate (DCA): complete the selected deep-dive labs on multi-stage builds, image optimization and container security.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 2, 5, $q$Docker Compose: multi-container stacks$q$, $q$• Declarative multi-container stack composition (docker-compose.yml)
• Internal DNS service discovery between containers
• The Compose labs from the Docker beginner course$q$, $q$KodeKloud Docker course: complete the Compose labs. Write a docker-compose.yml with two services that find each other by name.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 2, 6, $q$Compose: health checks and configuration$q$, $q$• Dependency chains with runtime health checks (depends_on with condition: service_healthy)
• Environment variable injection hierarchy$q$, $q$Extend your Compose file so the API waits for a healthy database (depends_on with condition: service_healthy), and test how values from .env files, the environment and the Compose file override each other.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 2, 7, $q$Week 2 milestone lab: a multi-tier stack$q$, $q$• Multi-tier architecture: frontend, API server, relational database and Redis cache
• Optimized multi-stage Dockerfiles for every tier
• NGINX reverse proxy in front of the stack with Docker Compose
• Explicit service dependency health checks and persistent storage volumes$q$, $q$Week 2 milestone lab. Containerize a multi-tier application architecture (frontend, API server, relational database, Redis cache) using optimized multi-stage Dockerfiles. Run the complete stack behind an NGINX reverse proxy managed via Docker Compose with explicit service dependency health checks and persistent storage volumes.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 3, 1, $q$CI/CD pipelines: workflows and builds$q$, $q$• GitHub Actions / Azure DevOps: workflow syntax
• Event-driven pipelines
• Parallel matrix builds
• Build artifact caching$q$, $q$KodeKloud GitHub Actions or Azure DevOps labs: complete the workflow syntax and matrix build labs. Build a workflow that runs tests in parallel across a matrix and caches its dependencies.$q$, $q$KodeKloud: GitHub Actions or Azure DevOps labs, AWS Cloud Practitioner / AWS IAM or AZ-104 labs, Terraform Basics Training Course, Cloud Playgrounds. Week 3 deliverable: an automated Terraform codebase provisioning cloud infrastructure, plus a CI/CD pipeline that builds and deploys containers via OIDC. About 10 to 12 hours this week.$q$ from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 3, 2, $q$CI/CD pipelines: secrets, OIDC and releases$q$, $q$• OpenID Connect (OIDC) keyless cloud authentication
• Secrets management
• Container build and push actions, registry pushes
• Vulnerability scanning
• Release strategies$q$, $q$KodeKloud GitHub Actions or Azure DevOps labs: complete the federated identity (OIDC), secrets management and container build/push labs. Add an image vulnerability scan to your workflow.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 3, 3, $q$Cloud foundations: identity and access$q$, $q$• IAM roles and least privilege policies
• AWS IAM or Entra ID basics
• The AWS Cloud Practitioner or AZ-104 IAM labs$q$, $q$KodeKloud AWS Cloud Practitioner / AWS IAM or AZ-104 Azure Administrator: fast-track the IAM or Entra ID labs in the AWS or Azure Playground. Create a role with a least-privilege policy.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 3, 4, $q$Cloud foundations: networking$q$, $q$• VPC/VNet layout
• Public and private subnets
• NAT Gateways
• Security Groups and NSGs$q$, $q$KodeKloud AWS or Azure Playground: build a VPC or VNet with a public and a private subnet, a NAT Gateway and a security group, following the course labs.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 3, 5, $q$Cloud foundations: compute, storage and monitoring$q$, $q$• Compute runtimes (EC2 or virtual machines)
• Managed storage (S3 or Blob Storage)
• Container registries (ECR or ACR)
• CloudWatch and Azure Monitor$q$, $q$KodeKloud AWS or Azure Playground: complete the EC2/VM, S3/Blob Storage and ECR/ACR labs, and look at the metrics and logs in CloudWatch or Azure Monitor.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 3, 6, $q$Terraform: HCL, variables and state$q$, $q$• Declarative HCL
• Variables
• State lifecycle (terraform.tfstate)
• Remote state with locking (S3 + DynamoDB or Azure Storage)$q$, $q$KodeKloud Terraform Basics Training Course: complete the HCL, state management, remote backend locking and variables labs.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 3, 7, $q$Terraform modules and the Week 3 milestone lab$q$, $q$• Reusable module design
• Plan and apply CI automation
• Provisioning a VPC/VNet, IAM roles and a container registry
• A CI/CD pipeline that tests, builds, scans and pushes an image with OIDC$q$, $q$Week 3 milestone lab. Using KodeKloud's Cloud Playground, write modular Terraform configurations with a remote state backend to provision a VPC/VNet, IAM roles, and a container registry. Construct a CI/CD pipeline in GitHub Actions or Azure DevOps that automatically tests, builds, scans, and pushes the container image via OIDC keyless authentication.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 4, 1, $q$Kubernetes architecture and workloads$q$, $q$• Control plane vs worker nodes
• Pods
• Deployments, StatefulSets, DaemonSets and Jobs
• Kubernetes for the Absolute Beginners: foundational labs$q$, $q$KodeKloud Kubernetes for the Absolute Beginners: fast-track the foundational architecture and core concept labs.$q$, $q$KodeKloud: Kubernetes for the Absolute Beginners, CKAD / CKA certification courses, Helm for Beginners, Kubernetes Playground. Week 4 deliverable: an end-to-end automated deployment of a custom Helm-packaged application onto a Kubernetes cluster via CI/CD. About 12 to 14 hours this week.$q$ from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 4, 2, $q$Kubernetes: configuration and traffic$q$, $q$• ConfigMaps and Secrets
• Services
• Ingress controllers$q$, $q$KodeKloud CKAD / CKA courses: complete the labs on Services, Ingress, ConfigMaps and Secrets.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 4, 3, $q$Kubernetes: storage and network policy$q$, $q$• PV, PVC and StorageClasses
• NetworkPolicies$q$, $q$KodeKloud CKAD / CKA courses: complete the labs on PV/PVC and NetworkPolicies.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 4, 4, $q$Kubernetes: scaling and health$q$, $q$• Horizontal Pod Autoscaler (HPA)
• Resource requests and limits
• Health probes$q$, $q$KodeKloud CKAD / CKA courses: complete the labs on HPA, resource limits and probes.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 4, 5, $q$Kubernetes troubleshooting$q$, $q$• kubectl debugging
• CrashLoopBackOff
• ImagePullBackOff
• Cluster troubleshooting (CKAD/CKA labs)$q$, $q$KodeKloud CKAD / CKA courses: complete the cluster troubleshooting labs. Break a deployment on purpose in the Kubernetes Playground and fix it using kubectl.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 4, 6, $q$Helm: charts and templates$q$, $q$• Chart structure (Chart.yaml, values.yaml, templates/)
• Go templating
• The chart anatomy labs from Helm for Beginners$q$, $q$KodeKloud Helm for Beginners: complete the labs on chart anatomy, Go templating and values.yaml overrides.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 4, 7, $q$Helm: environments and releases$q$, $q$• Environment overrides (values-dev.yaml, values-prod.yaml)
• Release operations: helm install, upgrade and rollback
• Third-party charts (ingress-nginx, cert-manager)$q$, $q$KodeKloud Helm for Beginners: complete the release upgrade and rollback labs. Install ingress-nginx and cert-manager from their charts.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 5, 1, $q$Capstone, part 1: deploy to Kubernetes with Helm$q$, $q$• Deploying a multi-service containerized application onto a multi-node Kubernetes cluster or an EKS/AKS sandbox
• Packaging the components into a custom Helm chart with environment-specific values$q$, $q$Week 4 capstone lab, part 1. Deploy the multi-service containerized application onto a KodeKloud multi-node Kubernetes cluster or managed EKS/AKS sandbox. Package components into a custom Helm chart using environment-specific values.$q$, $q$The capstone from the Week 4 plan (days 29 and 30).$q$ from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;

insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
select id, 5, 2, $q$Capstone, part 2: Ingress, HPA and CI/CD$q$, $q$• Ingress routing for the application
• HPA rules
• Automated deployment through the CI/CD pipeline$q$, $q$Week 4 capstone lab, part 2. Add Ingress routing and HPA rules to your Helm chart, and deploy it automatically through the CI/CD pipeline.$q$, null from lp_programs where lower(name) = 'devops' and not is_demo
on conflict (program_id, week, day) do update set topic = excluded.topic, topics_to_cover = excluded.topics_to_cover, practical_task = excluded.practical_task, resources = excluded.resources;
