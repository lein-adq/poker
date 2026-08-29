FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
ARG VITE_API_URL=http://localhost:4455
ARG VITE_KRATOS_URL=http://localhost:4433
ENV VITE_API_URL=$VITE_API_URL
ENV VITE_KRATOS_URL=$VITE_KRATOS_URL
RUN npm run build

FROM nginx:alpine
# SPA history-fallback routing; the stock config 404s on every client-side route.
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
