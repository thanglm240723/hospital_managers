// Cờ bật/tắt dữ liệu giả cho các màn chưa có API backend thật.
// Đặt REACT_APP_USE_MOCK_API=true trong .env.development.local (không commit) để bật khi phát triển.
// Không được bật mặc định ở production: build production luôn dùng false trừ khi biến môi trường được set khi build.
export const isMockApiEnabled = () => process.env.REACT_APP_USE_MOCK_API === 'true';

// Trễ giả lập mạng cho các client mock — giúp UI loading có ý nghĩa khi demo.
export const mockDelay = (value, ms = 300) => new Promise(resolve => setTimeout(() => resolve(value), ms));

export const mockReject = (problem, ms = 250) => new Promise((resolve, reject) => setTimeout(() => {
  const error = new Error(problem.title || 'Lỗi giả lập');
  error.response = { status: problem.status || 400, data: problem, headers: {} };
  reject(error);
}, ms));
