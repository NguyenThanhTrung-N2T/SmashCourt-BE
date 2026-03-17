# SmashCourt-BE 🏸

Backend API cho hệ thống đặt sân cầu lông **SmashCourt**, xây dựng bằng **ASP.NET Core 8** với Swagger UI.

## 🛠️ Tech Stack

| Thành phần | Công nghệ |
|---|---|
| Framework | ASP.NET Core 8 (Web API) |
| API Docs | Swagger / OpenAPI |
| Container | Docker |
| CI/CD | GitHub Actions |

## 📁 Cấu trúc thư mục

```
SmashCourt-BE/
├── .github/
│   └── workflows/
│       └── ci.yml          # GitHub Actions CI pipeline
├── Controllers/            # API Controllers
├── Properties/
├── Dockerfile              # Multi-stage Docker build
├── docker-compose.yml      # Docker Compose (có ngrok comment sẵn)
├── .dockerignore
├── .gitignore
├── appsettings.json
├── Program.cs
└── SmashCourt-BE.csproj
```

## 🚀 Chạy local (không dùng Docker)

**Yêu cầu:** .NET SDK 8.0

```bash
dotnet restore
dotnet run
```

Truy cập Swagger UI tại: `http://localhost:5000/swagger`

## 🐳 Chạy với Docker Desktop

```bash
docker-compose up --build
```

API sẽ chạy tại: `http://localhost:8080`

> **Ngrok:** Mở `docker-compose.yml`, bỏ comment phần `ngrok:` và thêm `NGROK_AUTHTOKEN` vào file `.env` khi cần expose ra ngoài.

## ⚙️ CI/CD

GitHub Actions tự động chạy mỗi khi **push** hoặc **pull request** vào nhánh `master` hoặc `develop`:

1. ✅ Restore dependencies
2. ✅ Build Release
3. ✅ Run Tests
4. ✅ Build Docker image

## 🔐 Biến môi trường

Tạo file `.env` từ mẫu (nếu có):

```bash
cp .env.example .env
```

| Biến | Mô tả |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | Môi trường chạy (`Production` / `Development`) |
| `NGROK_AUTHTOKEN` | Token xác thực Ngrok (khi dùng ngrok) |
