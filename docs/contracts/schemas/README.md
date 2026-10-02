# JSON Schemas của TADSS Decision API Contract

- `v2/` là contract hiện hành (INTEGRATION_SPEC): cặp `decision-request.schema.json` / `decision-result.schema.json` cho `POST /api/v2/decisions` và `decision-detail.schema.json` cho `GET /api/v2/decisions/{decisionId}`. Các schema viết tay này là nguồn chuẩn của contract.
- `../openapi-v2.yaml` được sinh từ Swashbuckle của chính app (không sửa tay) và có test lệch `OpenApiDriftTests`; khi API đổi có chủ đích, sinh lại theo hướng dẫn ở đầu test đó.
