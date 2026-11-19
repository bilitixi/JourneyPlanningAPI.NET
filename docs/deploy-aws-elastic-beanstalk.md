# Deploying to AWS (Elastic Beanstalk + RDS + CloudFront)

This sets up the API and the React frontend ([journeyplanninguidraft](https://github.com/bilitixi/journeyplanninguidraft))
on AWS:

```
                         https://dxxxx.cloudfront.net  (or your own domain)
                                       │
                                  CloudFront
                     ┌─────────────────┴──────────────────┐
              /api/* │                                    │ everything else
                     ▼                                    ▼
   Elastic Beanstalk (one EC2 instance,          S3 bucket (private)
   .NET 8 on Amazon Linux 2023)                  React build
                     │
                     ▼
     RDS PostgreSQL (private, same VPC)
```

- The browser only talks to CloudFront, which provides HTTPS. The frontend calls the API on the same address
  (`/api/...`), so no CORS setup is needed.
- The API runs on a single-instance Elastic Beanstalk environment, which has no load balancer and therefore no
  HTTPS of its own. CloudFront adds the HTTPS.
- GitHub Actions deploys both apps on every push to `main`, signing in to AWS with OIDC, so no AWS keys are
  stored in GitHub.

Rough monthly cost in us-east-1 at low traffic: EC2 t4g.micro about $6, its public IPv4 address about $3.65,
disk about $1, RDS db.t4g.micro about $12 plus 20 GB storage about $2.30, S3 and CloudFront under $1.
**Total about $25/month.** New AWS accounts get free-tier credits that cover this for a while. Check
current prices for your region.

Pick one region (for example `us-east-1`) and create everything below in it, except where noted.

## 1. Database: RDS PostgreSQL

RDS console → **Create database**:

| Setting | Value |
|---|---|
| Creation method | Standard create |
| Engine | PostgreSQL (latest 16 or 17 version) |
| Templates | Free tier (or Dev/Test) |
| Deployment | Single-AZ DB instance |
| DB instance identifier | `journey-planning-db` |
| Master username | `postgres` |
| Credentials management | Self managed. Set a strong password of letters and digits only, so it needs no escaping in a URL. |
| Instance class | `db.t4g.micro` |
| Storage | gp3, 20 GB, turn off storage autoscaling if you want a fixed bill |
| Compute resource | Don't connect to an EC2 compute resource |
| VPC | Default VPC |
| Public access | **No** |
| VPC security group | Create new: `journey-db-sg` |
| Additional configuration → Initial database name | `journey_planning` |
| Backup retention | 7 days |

When it's available, copy the **Endpoint** from the *Connectivity & security* tab. Your connection URL is:

```
postgresql://postgres:<PASSWORD>@<ENDPOINT>:5432/journey_planning?sslmode=require
```

The app creates its tables on first start.

## 2. API: Elastic Beanstalk

### 2a. Instance role (once per AWS account)

Elastic Beanstalk needs an IAM role for its EC2 instances. If `aws-elasticbeanstalk-ec2-role` doesn't exist yet in
IAM → Roles: **Create role** → *AWS service*, use case **EC2** → attach `AWSElasticBeanstalkWebTier` → name it
`aws-elasticbeanstalk-ec2-role`.

### 2b. Create the environment

Elastic Beanstalk console → **Create environment**:

| Setting | Value |
|---|---|
| Environment tier | Web server environment |
| Application name | `journey-planning` |
| Environment name | `journey-planning-api` |
| Domain | e.g. `journey-planning-api` → `journey-planning-api.<region>.elasticbeanstalk.com` |
| Platform | .NET Core on Linux, branch **.NET 8 running on 64bit Amazon Linux 2023** |
| Application code | Sample application (GitHub Actions deploys the real one) |
| Presets | **Single instance** |
| Service role | Create and use new service role |
| EC2 instance profile | `aws-elasticbeanstalk-ec2-role` |
| VPC | Default VPC; **Public IP address: activated**; pick all subnets |
| Database | Leave empty (the database from step 1 is separate, so it survives if you delete the environment) |
| Instance types | Architecture `arm64`, `t4g.micro` (or `x86_64`, `t3.micro`) |
| Health reporting | Enhanced (free) |
| Managed platform updates | Activated |

Under **Environment properties** add:

| Name | Value |
|---|---|
| `DATABASE_URL` | the URL from step 1 |
| `JWT_SECRET_KEY` | a long random string (e.g. `openssl rand -base64 48`). Reuse the Render value to keep existing logins valid. |
| `FRONTEND_URL` | leave for now; set it in step 4 |
| `OPENWEATHER_API_KEY` | your key |
| `OPENROUTER_API_KEY` | your key (optional) |
| `MAILGUN_API_KEY` + `MAILGUN_DOMAIN` | for verification/reset emails. SMTP (`MAIL_*` on port 587) also works from AWS. |

`PORT=5000` is set by `deploy/elasticbeanstalk/.ebextensions/01-environment.config`; don't change it.

Environment properties are visible to anyone with access to the Elastic Beanstalk console. That's acceptable for a
personal account; for a shared account, keep the secrets in AWS Secrets Manager instead.

### 2c. Let the API reach the database

1. EC2 console → **Instances** → the instance named `journey-planning-api` → **Security** tab. Note its security
   group (`awseb-e-...-AWSEBSecurityGroup-...`).
2. EC2 → **Security Groups** → `journey-db-sg` → **Edit inbound rules** → add *PostgreSQL* (TCP 5432) with that
   security group as the source. Remove any other inbound rule there.

## 3. Frontend: S3 bucket and CloudFront

### 3a. S3 bucket

S3 → **Create bucket**: a globally unique name such as `journey-planning-frontend-<something>`, same region, **Block
all public access: on** (the default). Don't turn on static website hosting; CloudFront reads the bucket directly.

### 3b. CloudFront function for page routes

The React app uses routes like `/dashboard` and `/journeys/5` that don't exist as files. This function serves
`index.html` for them. CloudFront's "custom error pages" would also do that, but they would replace the API's own
404 responses too.

CloudFront → **Functions** → **Create function** `spa-rewrite` (runtime `cloudfront-js-2.0`), paste the code from
[`aws/cloudfront-spa-rewrite.js`](https://github.com/bilitixi/journeyplanninguidraft/blob/main/aws/cloudfront-spa-rewrite.js)
in the frontend repo, **Save**, then **Publish**.

### 3c. Distribution

CloudFront → **Create distribution**. If the console asks you to choose a pricing plan, pay-as-you-go costs pennies at
this traffic.

Default origin and behavior (the frontend):

| Setting | Value |
|---|---|
| Origin domain | the S3 bucket |
| Origin access | **Origin access control settings** → create a new OAC |
| Viewer protocol policy | Redirect HTTP to HTTPS |
| Cache policy | CachingOptimized |
| Function associations → Viewer request | CloudFront Functions → `spa-rewrite` |
| Web Application Firewall | Do not enable (it costs extra) |
| Default root object | `index.html` |

After creating it, CloudFront shows a bucket policy to copy: paste it into the bucket's **Permissions → Bucket
policy**.

Then add the API. In the distribution:

1. **Origins → Create origin**: origin domain `journey-planning-api.<region>.elasticbeanstalk.com`, protocol
   **HTTP only**, port 80, response timeout **60** seconds (AI recommendations can be slow).
2. **Behaviors → Create behavior**:

| Setting | Value |
|---|---|
| Path pattern | `/api/*` |
| Origin | the Elastic Beanstalk origin |
| Viewer protocol policy | Redirect HTTP to HTTPS |
| Allowed HTTP methods | GET, HEAD, OPTIONS, PUT, POST, PATCH, DELETE |
| Cache policy | **CachingDisabled** |
| Origin request policy | **AllViewerExceptHostHeader** (passes the `Authorization` header and query strings) |

Note the distribution's domain name (`dxxxx.cloudfront.net`) and ID.

## 4. Point the API's email links at the site

Elastic Beanstalk → environment → **Configuration → Updates, monitoring, and logging → Environment properties**: set
`FRONTEND_URL` to `https://dxxxx.cloudfront.net` (or your own domain from step 7).

## 5. Let GitHub Actions deploy

### 5a. OIDC provider (once per AWS account)

IAM → **Identity providers → Add provider**: OpenID Connect, URL `https://token.actions.githubusercontent.com`,
audience `sts.amazonaws.com`.

### 5b. Deploy role

IAM → **Roles → Create role** → *Web identity*, provider `token.actions.githubusercontent.com`, audience
`sts.amazonaws.com`, GitHub organization `bilitixi` → name it `github-deploy-journey-planning`. Then
replace its **Trust relationships** with the following, so only pushes to `main` in these two repos can use it:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::<ACCOUNT_ID>:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": { "token.actions.githubusercontent.com:aud": "sts.amazonaws.com" },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": [
            "repo:bilitixi/JourneyPlanningAPI.NET:ref:refs/heads/main",
            "repo:bilitixi/journeyplanninguidraft:ref:refs/heads/main"
          ]
        }
      }
    }
  ]
}
```

Permissions:

- Attach the managed policy `AdministratorAccess-AWSElasticBeanstalk` (needed to deploy to Elastic Beanstalk).
- Add an inline policy for the frontend deploy:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    { "Effect": "Allow", "Action": "s3:ListBucket", "Resource": "arn:aws:s3:::<BUCKET>" },
    { "Effect": "Allow", "Action": ["s3:PutObject", "s3:DeleteObject"], "Resource": "arn:aws:s3:::<BUCKET>/*" },
    {
      "Effect": "Allow",
      "Action": "cloudfront:CreateInvalidation",
      "Resource": "arn:aws:cloudfront::<ACCOUNT_ID>:distribution/<DISTRIBUTION_ID>"
    }
  ]
}
```

### 5c. Repository variables

In each GitHub repo: **Settings → Secrets and variables → Actions → Variables** (these are not secrets: the role
can only be used from these repos' `main` branch).

| Repo | Variable | Value |
|---|---|---|
| both | `AWS_REGION` | e.g. `us-east-1` |
| both | `AWS_ROLE_ARN` | `arn:aws:iam::<ACCOUNT_ID>:role/github-deploy-journey-planning` |
| JourneyPlanningAPI.NET | `EB_APPLICATION` | `journey-planning` |
| JourneyPlanningAPI.NET | `EB_ENVIRONMENT` | `journey-planning-api` |
| journeyplanninguidraft | `S3_BUCKET` | the bucket name |
| journeyplanninguidraft | `CLOUDFRONT_DISTRIBUTION_ID` | the distribution ID |

The deploy workflows skip themselves until `EB_ENVIRONMENT` / `S3_BUCKET` are set.

## 6. First deploy and check

Run **Actions → Deploy to AWS Elastic Beanstalk → Run workflow** in this repo, and **Deploy to AWS (S3 +
CloudFront)** in the frontend repo (or push to `main`). Then:

- `https://dxxxx.cloudfront.net/api/journeys` should return `{"error": "Token is missing"}`: CloudFront reaches the
  API. (The API exits at startup if it can't reach the database, so a running API also means the database is fine.)
- `https://dxxxx.cloudfront.net/` should show the login page. Register, check the verification email, log in and
  create a journey.

If the environment turns red or the deploy fails: Elastic Beanstalk → environment → **Logs → Request logs → Last 100
lines**, and look at `web.stdout.log`. A database problem shows up as `Could not initialise the database at ...`,
usually a wrong `DATABASE_URL` or the security group rule from step 2c.

## 7. Optional

- **Your own domain** (e.g. `journeyplanningapp.bilitixi.org`): request a certificate in **ACM in us-east-1**
  (CloudFront only uses certificates from that region), add the domain under the distribution's *Alternate domain
  names* with that certificate, and create a CNAME record at your DNS provider pointing to `dxxxx.cloudfront.net`.
  Then update `FRONTEND_URL`.
- **Only allow traffic through CloudFront:** in the Elastic Beanstalk instance's security group, replace the inbound
  HTTP rule from `0.0.0.0/0` with the managed prefix list `com.amazonaws.global.cloudfront.origin-facing`.
  Otherwise the API can also be reached over plain HTTP at the `elasticbeanstalk.com` address.
- **Moving data from Render:** `pg_dump` from Render's external database URL, then restore into RDS. The RDS database
  isn't reachable from the internet, so either restore from the Elastic Beanstalk instance, or briefly set the
  database's *Public access* to Yes with an inbound rule for your IP only, restore, and turn it off again.
- **Turning it off:** terminating the Elastic Beanstalk environment stops the EC2 and IP charges; the RDS database
  must be stopped or deleted separately (a stopped database restarts automatically after 7 days).
