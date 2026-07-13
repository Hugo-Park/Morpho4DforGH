#include <cmath>
#include <vector>
#include <omp.h>

#define DLLEXPORT extern "C" __declspec(dllexport)

const double PI = 3.14159265358979323846;

struct Vector3 {
    double x, y, z;
    Vector3() : x(0), y(0), z(0) {}
    Vector3(double _x, double _y, double _z) : x(_x), y(_y), z(_z) {}
    Vector3 operator+(const Vector3& o) const { return Vector3(x + o.x, y + o.y, z + o.z); }
    Vector3 operator-(const Vector3& o) const { return Vector3(x - o.x, y - o.y, z - o.z); }
    Vector3 operator-() const { return Vector3(-x, -y, -z); }
    Vector3 operator*(double s) const { return Vector3(x * s, y * s, z * s); }
    double length() const { return std::sqrt(x * x + y * y + z * z); }
    void unitize() { double l = length(); if (l > 1e-9) { x /= l; y /= l; z /= l; } }
};

inline double dot(const Vector3& a, const Vector3& b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
inline Vector3 cross(const Vector3& a, const Vector3& b) {
    return Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
}
inline double angle(Vector3 a, Vector3 b) {
    a.unitize(); b.unitize();
    double d = dot(a, b);
    if (d < -1.0) d = -1.0;
    if (d > 1.0) d = 1.0;
    return std::acos(d);
}

DLLEXPORT void OptimizeMorpho(
    int numVoxels, double* coords, const int* isFixed, const double* loads,
    int numSprings, const int* springIds, const double* springParams,
    int numHinges, const int* hingeIds, const double* hingeParams,
    int maxIterations
) {
    std::vector<Vector3> grad(numVoxels);
    
    // Adam Optimizer 상태 변수
    std::vector<Vector3> m(numVoxels, Vector3(0,0,0));
    std::vector<Vector3> v(numVoxels, Vector3(0,0,0));
    
    // [Convergence Patch]
    // C#에서 넘어오는 150번의 반복 횟수는 L-BFGS에는 충분하지만 Adam에는 턱없이 부족합니다.
    // 90도로 접히기 전에 조기 종료되는 현상을 막기 위해 반복 횟수와 속도를 강제로 늘립니다.
    maxIterations = 2000; 
    double alpha = 0.05; // 학습률 — 0.2에서 0.05로 낮춤 (오버슈팅으로 복셀 비산 방지)
    
    double beta1 = 0.9;
    double beta2 = 0.999;
    double epsilon = 1e-8;


    // 최소 스프링 길이 계산 (한 스텝당 최대 이동거리 제한에 사용)
    double minSpringLen = 1e9;
    for (int i = 0; i < numSprings; ++i) {
        double pl = springParams[i * 3 + 0];
        if (pl > 1e-9 && pl < minSpringLen) minSpringLen = pl;
    }
    if (minSpringLen > 1e8) minSpringLen = 1.0;
    double maxStep = minSpringLen * 0.1; // 한 스텝당 스프링 길이의 10%까지만 이동

    for (int iter = 1; iter <= maxIterations; ++iter) {
        
        // 1. Force(물리적 힘) 배열 초기화 및 외부 하중 적용
        for (int i = 0; i < numVoxels; ++i) {
            grad[i] = Vector3(0, 0, 0);
            if (isFixed[i]) continue;
            // [부호 확인 완료] grad는 "힘(Force)" 벡터를 누적합니다.
            // Adam 업데이트: g = -grad, coords -= α·m̂  →  결과적으로 coords는 Force 방향으로 이동
            // 따라서 grad += loads 가 올바른 부호입니다.
            grad[i].x += loads[i * 3 + 0];
            grad[i].y += loads[i * 3 + 1];
            grad[i].z += loads[i * 3 + 2];
        }

        // 2. 스프링 포스 계산 (OpenMP)
        #pragma omp parallel for
        for (int i = 0; i < numSprings; ++i) {
            int a = springIds[i * 2 + 0];
            int b = springIds[i * 2 + 1];
            
            // C# packing: [pairLength, cachedTargetDist, cachedK] (stride 3)
            double targetLength = springParams[i * 3 + 1];
            double k = springParams[i * 3 + 2];

            Vector3 vA(coords[a * 3 + 0], coords[a * 3 + 1], coords[a * 3 + 2]);
            Vector3 vB(coords[b * 3 + 0], coords[b * 3 + 1], coords[b * 3 + 2]);
            Vector3 dir = vB - vA;
            double currentLength = std::sqrt(dir.x*dir.x + dir.y*dir.y + dir.z*dir.z);

            if (currentLength > 1e-9) {
                dir.x /= currentLength; dir.y /= currentLength; dir.z /= currentLength;
                double forceMag = k * (currentLength - targetLength);
                Vector3 force = dir * forceMag;

                #pragma omp atomic
                grad[a].x += force.x;
                #pragma omp atomic
                grad[a].y += force.y;
                #pragma omp atomic
                grad[a].z += force.z;
                
                #pragma omp atomic
                grad[b].x -= force.x;
                #pragma omp atomic
                grad[b].y -= force.y;
                #pragma omp atomic
                grad[b].z -= force.z;
            }
        }

        // 3. 힌지 포스 계산 (OpenMP)
        #pragma omp parallel for
        for (int i = 0; i < numHinges; ++i) {
            int L = hingeIds[i * 3 + 0];
            int C = hingeIds[i * 3 + 1];
            int R = hingeIds[i * 3 + 2];
            
            // C# packing: [targetAngle, refNorm.X, refNorm.Y, refNorm.Z, cachedK] (stride 5)
            double targetAngle = hingeParams[i * 5 + 0];
            Vector3 refNorm(hingeParams[i * 5 + 1], hingeParams[i * 5 + 2], hingeParams[i * 5 + 3]);
            double k = hingeParams[i * 5 + 4];

            Vector3 ptL(coords[L*3], coords[L*3+1], coords[L*3+2]);
            Vector3 ptC(coords[C*3], coords[C*3+1], coords[C*3+2]);
            Vector3 ptR(coords[R*3], coords[R*3+1], coords[R*3+2]);

            Vector3 vL = ptL - ptC;
            Vector3 vR = ptR - ptC;
            double lenL = vL.length();
            double lenR = vR.length();

            if (lenL >= 1e-9 && lenR >= 1e-9) {
                double curAngle = angle(vL, vR);
                Vector3 cCross = cross(vL, vR);
                if (dot(cCross, refNorm) < 0) curAngle = 2.0 * PI - curAngle;

                double torqueMag = k * (curAngle - targetAngle);
                
                Vector3 normal = cCross;
                if (normal.length() < 1e-4) normal = refNorm;
                else {
                    normal.unitize();
                    if (dot(normal, refNorm) < 0) normal = -normal;
                }

                Vector3 dirL = cross(normal, vL); dirL.unitize();
                Vector3 dirR = cross(vR, normal); dirR.unitize();

                Vector3 fL = dirL * (torqueMag / lenL);
                Vector3 fR = dirR * (torqueMag / lenR);
                Vector3 fC = -(fL + fR);

                #pragma omp atomic
                grad[L].x += fL.x;
                #pragma omp atomic
                grad[L].y += fL.y;
                #pragma omp atomic
                grad[L].z += fL.z;
                
                #pragma omp atomic
                grad[C].x += fC.x;
                #pragma omp atomic
                grad[C].y += fC.y;
                #pragma omp atomic
                grad[C].z += fC.z;
                
                #pragma omp atomic
                grad[R].x += fR.x;
                #pragma omp atomic
                grad[R].y += fR.y;
                #pragma omp atomic
                grad[R].z += fR.z;
            }
        }

        // 4. Adam 최적화 업데이트 (극단적인 강성 차이 극복)
        for (int i = 0; i < numVoxels; ++i) {
            if (isFixed[i]) continue;
            
            // 수학적 그래디언트는 물리적 힘의 반대 (G = -Force)
            Vector3 g(-grad[i].x, -grad[i].y, -grad[i].z);
            
            m[i].x = beta1 * m[i].x + (1.0 - beta1) * g.x;
            m[i].y = beta1 * m[i].y + (1.0 - beta1) * g.y;
            m[i].z = beta1 * m[i].z + (1.0 - beta1) * g.z;
            
            v[i].x = beta2 * v[i].x + (1.0 - beta2) * g.x * g.x;
            v[i].y = beta2 * v[i].y + (1.0 - beta2) * g.y * g.y;
            v[i].z = beta2 * v[i].z + (1.0 - beta2) * g.z * g.z;
            
            double m_hat_x = m[i].x / (1.0 - std::pow(beta1, iter));
            double m_hat_y = m[i].y / (1.0 - std::pow(beta1, iter));
            double m_hat_z = m[i].z / (1.0 - std::pow(beta1, iter));
            
            double v_hat_x = v[i].x / (1.0 - std::pow(beta2, iter));
            double v_hat_y = v[i].y / (1.0 - std::pow(beta2, iter));
            double v_hat_z = v[i].z / (1.0 - std::pow(beta2, iter));
            
            // [Stiffness Locking 돌파 패치 - Standard Adam 복구]
            // 물리 방향성을 강제로 맞추면(L2 norm), PLA의 막대한 X축 복원력(3000) 때문에 
            // 눕기 위해 필요한 미세한 Z축 힘(1)이 완전히 무시되어(step_z = 0.00001) 절대 펴지지 않습니다.
            // Adam의 원래 방식대로 X, Y, Z를 독립적으로 정규화해야 미세한 Z축 힘도 증폭되어 
            // 단단한 벽이 허공에서 곡선을 그리며 누울 수 있습니다!
            double epsilon_adam = 1.0; 
            
            double step_x = alpha * m_hat_x / (std::sqrt(v_hat_x) + epsilon_adam);
            double step_y = alpha * m_hat_y / (std::sqrt(v_hat_y) + epsilon_adam);
            double step_z = alpha * m_hat_z / (std::sqrt(v_hat_z) + epsilon_adam);
            
            // [이동거리 제한] 한 스텝에 스프링 길이의 10% 이상 이동 금지 → 복셀 비산 방지
            double stepLen = std::sqrt(step_x*step_x + step_y*step_y + step_z*step_z);
            if (stepLen > maxStep) {
                double scale = maxStep / stepLen;
                step_x *= scale;
                step_y *= scale;
                step_z *= scale;
            }
            
            coords[i * 3 + 0] -= step_x;
            coords[i * 3 + 1] -= step_y;
            coords[i * 3 + 2] -= step_z;
        }
    }
}
