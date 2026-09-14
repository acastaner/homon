# Build from the repository root:
#   docker build -f src/Homon.Web/Dockerfile \
#     --build-arg VITE_APP_VERSION=0.1.0 \
#     -t homon-web .
#
# NO API HOSTNAME IS BAKED INTO THIS IMAGE, and that is deliberate. The SPA and the API are
# served from ONE ORIGIN: nginx.conf in this same image sends `/api/` to the api container
# and everything else to the bundle, so the client calls the relative path `/api/v1`
# (src/Homon.Web/src/lib/api.ts, whose fallback is that literal). There is therefore nothing
# for Vite to inline, and a change of hostname is a reverse-proxy edit rather than a rebuild.

FROM node:24-alpine AS build
WORKDIR /app

COPY src/Homon.Web/package.json src/Homon.Web/package-lock.json ./
RUN npm ci

COPY src/Homon.Web/ ./

# The release version, inlined by Vite. Supplied by .github/workflows/release.yml as the
# tag name with its leading `v` stripped, matching the image tag GHCR carries.
#
# A local `docker build` with no --build-arg leaves the ARG unset, but the ENV line below
# still runs — so Vite inlines an EMPTY STRING, not `undefined`. The footer's fallback to
# "dev" (src/components/app-shell.tsx) treats that empty string as absent (`||`, not `??`)
# for exactly this reason.
ARG VITE_APP_VERSION
ENV VITE_APP_VERSION=$VITE_APP_VERSION

RUN npm run build

FROM nginx:1-alpine AS runtime
COPY src/Homon.Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
