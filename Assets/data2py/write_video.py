import cv2
import os

def write_vedio(folder_path, vedio_name, save_path, vedio_fps=30):
    vedio_path = os.path.join(save_path, vedio_name)
    vedio_writer = cv2.VideoWriter(vedio_path, cv2.VideoWriter_fourcc(*'mp4v'), vedio_fps, (640, 480))
    
    datas = os.listdir(folder_path)
    datas.sort()
    imgs = [img for img in datas if img.endswith(".png")]
    end = 285
    for img in imgs[:end]:
        img_path = os.path.join(folder_path, img)
        frame = cv2.imread(img_path)
        vedio_writer.write(frame)
    vedio_writer.release()
    print("Vedio saved at: ", vedio_path)

if __name__ == "__main__":
    folder_prefix_path = "../Captures/Camera_"
    camera_num = 6
    save_path = "../Captures/videos/"
    if not os.path.exists(save_path):
        os.makedirs(save_path)
    for i in range(camera_num):
        write_vedio(f"{folder_prefix_path}{i}", f"camera_{i}.mp4", save_path)
        
# 0508_p1: 317, 333, 489, 507, 473
# 0509_p1: 295, 298, 261
# 0509_p2: 272, 255, 285