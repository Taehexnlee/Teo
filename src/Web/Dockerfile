# ---- build stage ----
    FROM node:20-alpine AS build
    WORKDIR /app
    COPY package*.json ./
    RUN npm ci
    COPY . .
    # compose 에서 build.args로 VITE_API_BASE를 주입할 거예요.
    ARG VITE_API_BASE
    ENV VITE_API_BASE=${VITE_API_BASE}
    RUN npm run build
    
    # ---- run stage (nginx) ----
    FROM nginx:1.27-alpine
    COPY --from=build /app/dist /usr/share/nginx/html
    # SPA 라우팅을 위해 try_files 설정된 nginx.conf 사용
# 기존: COPY /nginx/nginx.conf /etc/nginx/conf.d/default.conf
    COPY nginx/nginx.conf /etc/nginx/conf.d/default.conf
    EXPOSE 80
    CMD ["nginx", "-g", "daemon off;"]